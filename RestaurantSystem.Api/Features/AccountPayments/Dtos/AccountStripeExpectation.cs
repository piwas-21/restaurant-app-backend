namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountStripeExpectation
{
    public required Guid AttemptId { get; init; }
    public required AccountStripeContext Context { get; init; }
    public required long AmountMinor { get; init; }
    public required string Currency { get; init; }
    public string? SessionId { get; init; }
    public string? IntentId { get; init; }
}
