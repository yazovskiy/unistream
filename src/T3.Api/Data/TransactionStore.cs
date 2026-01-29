using Npgsql;
using NpgsqlTypes;
using T3.Api.Models;

namespace T3.Api.Data;

internal interface ITransactionStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<TransactionStored?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CreateResult> CreateAsync(Transaction transaction, DateTimeOffset insertDateTime, bool strictIdempotency, CancellationToken cancellationToken);
}

internal sealed record CreateResult(
    bool Inserted,
    bool CapacityReached,
    bool Conflict,
    DateTimeOffset? InsertDateTime,
    TransactionStored? Existing
);

internal sealed class TransactionStore : ITransactionStore
{
    private readonly string _connectionString;

    public TransactionStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
CREATE TABLE IF NOT EXISTS transactions (
  id UUID PRIMARY KEY,
  transaction_date TIMESTAMPTZ NOT NULL,
  amount NUMERIC(18,2) NOT NULL,
  insert_date_time TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS capacity (
  id INT PRIMARY KEY,
  count INT NOT NULL
);

INSERT INTO capacity (id, count)
VALUES (1, 0)
ON CONFLICT (id) DO NOTHING;
";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TransactionStored?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await GetByIdAsync(connection, null, id, cancellationToken);
    }

    public async Task<CreateResult> CreateAsync(Transaction transaction, DateTimeOffset insertDateTime, bool strictIdempotency, CancellationToken cancellationToken)
    {
        var storedCandidate = ToStored(transaction, insertDateTime);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transactionScope = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await GetByIdAsync(connection, transactionScope, storedCandidate.Id, cancellationToken);
        if (existing is not null)
        {
            // Идемпотентность: возвращаем уже сохранённый результат.
            await transactionScope.CommitAsync(cancellationToken);
            if (strictIdempotency && !IsSame(existing, storedCandidate))
            {
                return new CreateResult(
                    Inserted: false,
                    CapacityReached: false,
                    Conflict: true,
                    InsertDateTime: existing.InsertDateTime,
                    Existing: existing
                );
            }

            return new CreateResult(
                Inserted: false,
                CapacityReached: false,
                Conflict: false,
                InsertDateTime: existing.InsertDateTime,
                Existing: existing
            );
        }

        // Проверяем лимит (макс. 100 записей).
        var capacityUpdated = await UpdateCapacityAsync(connection, transactionScope, cancellationToken);
        if (!capacityUpdated)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return new CreateResult(
                Inserted: false,
                CapacityReached: true,
                Conflict: false,
                InsertDateTime: null,
                Existing: null
            );
        }

        try
        {
            await InsertAsync(connection, transactionScope, storedCandidate, cancellationToken);
            await transactionScope.CommitAsync(cancellationToken);
            return new CreateResult(
                Inserted: true,
                CapacityReached: false,
                Conflict: false,
                InsertDateTime: storedCandidate.InsertDateTime,
                Existing: null
            );
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            var existingAfter = await GetByIdAsync(storedCandidate.Id, cancellationToken);
            if (existingAfter is null)
            {
                throw;
            }

            if (strictIdempotency && !IsSame(existingAfter, storedCandidate))
            {
                // В строгом режиме несовпадение данных — конфликт.
                return new CreateResult(
                    Inserted: false,
                    CapacityReached: false,
                    Conflict: true,
                    InsertDateTime: existingAfter.InsertDateTime,
                    Existing: existingAfter
                );
            }

            return new CreateResult(
                Inserted: false,
                CapacityReached: false,
                Conflict: false,
                InsertDateTime: existingAfter.InsertDateTime,
                Existing: existingAfter
            );
        }
    }

    private static async Task<TransactionStored?> GetByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT id, transaction_date, amount, insert_date_time
FROM transactions
WHERE id = @id;
";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TransactionStored(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetDecimal(2),
            reader.GetFieldValue<DateTimeOffset>(3)
        );
    }

    private static async Task<bool> UpdateCapacityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Атомарно увеличиваем счётчик, чтобы избежать гонок между инстансами.
        const string sql = @"
UPDATE capacity
SET count = count + 1
WHERE id = 1 AND count < 100;
";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TransactionStored entity,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO transactions (id, transaction_date, amount, insert_date_time)
VALUES (@id, @transaction_date, @amount, @insert_date_time);
";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, entity.Id);
        command.Parameters.AddWithValue("transaction_date", NpgsqlDbType.TimestampTz, entity.TransactionDate);
        command.Parameters.AddWithValue("amount", NpgsqlDbType.Numeric, entity.Amount);
        command.Parameters.AddWithValue("insert_date_time", NpgsqlDbType.TimestampTz, entity.InsertDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsSame(TransactionStored existing, TransactionStored candidate)
    {
        return existing.Amount == candidate.Amount
            && existing.TransactionDate.ToUniversalTime() == candidate.TransactionDate.ToUniversalTime();
    }

    private static TransactionStored ToStored(Transaction transaction, DateTimeOffset insertDateTime)
    {
        return new TransactionStored(
            transaction.Id,
            NormalizeTransactionDate(transaction.TransactionDate),
            transaction.Amount,
            insertDateTime
        );
    }

    private static DateTimeOffset NormalizeTransactionDate(DateTime value)
    {
        // Нормализация DateTime перед сохранением.
        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
        };
    }
}
