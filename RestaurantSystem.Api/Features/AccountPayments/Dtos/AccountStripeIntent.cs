namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountStripeIntent
{
    public required string Id { get; init; }
    public required AccountStripeContext Context { get; init; }
    public required string Status { get; init; }
    public required long AmountMinor { get; init; }
    public required long ReceivedMinor { get; init; }
    public required string Currency { get; init; }
    public string? ChargeId { get; init; }
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}
