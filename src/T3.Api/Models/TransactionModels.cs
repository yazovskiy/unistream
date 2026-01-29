namespace T3.Api.Models;

// Публичная модель транзакции строго по заданию.
public sealed record Transaction(Guid Id, DateTime TransactionDate, decimal Amount);

public sealed record TransactionInsertResponse(DateTimeOffset InsertDateTime);

// Внутренняя модель для хранения и идемпотентности.
internal sealed record TransactionStored(
    Guid Id,
    DateTimeOffset TransactionDate,
    decimal Amount,
    DateTimeOffset InsertDateTime
);
