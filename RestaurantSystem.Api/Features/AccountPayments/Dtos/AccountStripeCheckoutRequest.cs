namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Persist this exact provider payload and key before sending it to Stripe.</summary>
public sealed record AccountStripeCheckoutRequest
{
    public required Guid AttemptId { get; init; }
    public required AccountStripeContext Context { get; init; }
    public required long AmountMinor { get; init; }
    public required string Currency { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string IdempotencyKey { get; init; }
}
