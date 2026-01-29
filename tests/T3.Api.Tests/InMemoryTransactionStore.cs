using System.Collections.Concurrent;
using T3.Api.Data;
using T3.Api.Models;

namespace T3.Api.Tests;

internal sealed class InMemoryTransactionStore : ITransactionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, TransactionStored> _items = new();
    private readonly bool _strictIdempotency;
    private int _count;

    public InMemoryTransactionStore(bool strictIdempotency)
    {
        _strictIdempotency = strictIdempotency;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Ничего не инициализируем, это in-memory хранилище.
        return Task.CompletedTask;
    }

    public Task<TransactionStored?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // Простой потокобезопасный доступ к словарю.
        lock (_lock)
        {
            _items.TryGetValue(id, out var stored);
            return Task.FromResult(stored);
        }
    }

    public Task<CreateResult> CreateAsync(Transaction transaction, DateTimeOffset insertDateTime, bool strictIdempotency, CancellationToken cancellationToken)
    {
        var candidate = ToStored(transaction, insertDateTime);
        lock (_lock)
        {
            if (_items.TryGetValue(candidate.Id, out var existing))
            {
                // Имитация идемпотентности и строгого режима.
                if (_strictIdempotency && !IsSame(existing, candidate))
                {
                    return Task.FromResult(new CreateResult(false, false, true, existing.InsertDateTime, existing));
                }

                return Task.FromResult(new CreateResult(false, false, false, existing.InsertDateTime, existing));
            }

            if (_count >= 100)
            {
                // Имитация лимита ёмкости.
                return Task.FromResult(new CreateResult(false, true, false, null, null));
            }

            _items[candidate.Id] = candidate;
            _count++;
            return Task.FromResult(new CreateResult(true, false, false, candidate.InsertDateTime, null));
        }
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
        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
        };
    }
}
