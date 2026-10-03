namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Canonical provider evidence, not a client redirect success claim.</summary>
public sealed record AccountStripeSession
{
    public required string Id { get; init; }
    public required AccountStripeContext Context { get; init; }
    public string? Url { get; init; }
    public required string Status { get; init; }
    public required string PaymentStatus { get; init; }
    public long? AmountMinor { get; init; }
    public string? Currency { get; init; }
    public string? IntentId { get; init; }
    public string? ClientReferenceId { get; init; }
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}
