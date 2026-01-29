using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using T3.Api.Data;
using T3.Api.Models;
using T3.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TransactionOptions>(builder.Configuration.GetSection("TransactionOptions"));

builder.Services.AddSingleton<ITransactionStore, TransactionStore>();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// Создаём таблицы при старте (только для тестового задания).
await app.Services.GetRequiredService<ITransactionStore>()
    .InitializeAsync(app.Lifetime.ApplicationStopping);

app.MapPost("/api/v1/Transaction", async (
    Transaction request,
    ITransactionStore store,
    IOptions<TransactionOptions> options,
    CancellationToken cancellationToken) =>
{
    // Валидация входных данных.
    var validationErrors = Validate(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors, statusCode: StatusCodes.Status400BadRequest,
            title: "Validation error",
            type: "urn:problem-type:validation");
    }

    // Фиксируем время вставки для идемпотентного ответа.
    var insertDateTime = DateTimeOffset.UtcNow;

    var result = await store.CreateAsync(request, insertDateTime, options.Value.StrictIdempotency, cancellationToken);

    if (result.CapacityReached)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Transaction capacity reached",
            type: "urn:problem-type:capacity",
            detail: "The service stores at most 100 transactions."
        );
    }

    if (result.Conflict)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Idempotency conflict",
            type: "urn:problem-type:idempotency-conflict",
            detail: "A transaction with the same Id already exists but has different data."
        );
    }

    var responseInsertDateTime = result.InsertDateTime ?? insertDateTime;
    return Results.Ok(new TransactionInsertResponse(responseInsertDateTime));
});

app.MapGet("/api/v1/Transaction", async (
    Guid id,
    ITransactionStore store,
    CancellationToken cancellationToken) =>
{
    var stored = await store.GetByIdAsync(id, cancellationToken);
    if (stored is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Transaction not found",
            type: "urn:problem-type:not-found",
            detail: "No transaction exists for the provided id."
        );
    }

    // Возвращаем только публичную модель из задания.
    var response = new Transaction(stored.Id, stored.TransactionDate.UtcDateTime, stored.Amount);
    return Results.Ok(response);
});

app.Run();

static Dictionary<string, string[]> Validate(Transaction request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    if (request.Amount <= 0)
    {
        errors["amount"] = ["Amount must be positive."];
    }

    var normalizedDate = NormalizeTransactionDate(request.TransactionDate);
    if (normalizedDate > DateTimeOffset.UtcNow)
    {
        errors["transactionDate"] = ["Transaction date cannot be in the future."];
    }

    if (request.Id == Guid.Empty)
    {
        errors["id"] = ["Id must be a non-empty GUID."];
    }

    return errors;
}

static DateTimeOffset NormalizeTransactionDate(DateTime value)
{
    return value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(value),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
    };
}

// Требуется для WebApplicationFactory в тестах.
public partial class Program { }
