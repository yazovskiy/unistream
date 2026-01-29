namespace T3.Api.Options;

public sealed class TransactionOptions
{
    public bool StrictIdempotency { get; init; } = false;
}
