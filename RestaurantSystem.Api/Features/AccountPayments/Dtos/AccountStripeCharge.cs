namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Canonical charge evidence; no card, billing, receipt URL or customer data.</summary>
public sealed record AccountStripeCharge
{
    public required string Id { get; init; }
    public required AccountStripeContext Context { get; init; }
    public required string IntentId { get; init; }
    public required string Status { get; init; }
    public required long AmountMinor { get; init; }
    public required long CapturedMinor { get; init; }
    public required long RefundedMinor { get; init; }
    public required string Currency { get; init; }
    public required bool Paid { get; init; }
    public required bool Captured { get; init; }
    public required bool Disputed { get; init; }
}
