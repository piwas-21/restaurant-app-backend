namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record AmendmentRefundRequest(
    string ChargeId,
    string IntentId,
    string Currency,
    long AmountMinor,
    string IdempotencyKey,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record AmendmentRefundProviderContext(string ConnectedAccountId, bool LiveMode);

public sealed record AmendmentRefundEvidence(
    string RefundId,
    string ChargeId,
    string IntentId,
    long AmountMinor,
    string Currency,
    string Status,
    AmendmentRefundProviderContext Context,
    IReadOnlyDictionary<string, string> Metadata);
