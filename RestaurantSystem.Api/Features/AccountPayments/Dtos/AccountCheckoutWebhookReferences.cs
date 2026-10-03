namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Provider identifiers from a signed event; never used as money evidence.</summary>
public sealed record AccountCheckoutWebhookReferences(
    Guid? AttemptId,
    string? SessionId,
    string? IntentId,
    string? ChargeId)
{
    public bool HasAny => AttemptId.HasValue || SessionId is not null || IntentId is not null || ChargeId is not null;
}
