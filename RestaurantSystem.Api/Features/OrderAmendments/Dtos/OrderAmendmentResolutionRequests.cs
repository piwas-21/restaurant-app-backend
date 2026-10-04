using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentResolutionQuoteRequest
{
    [JsonRequired]
    public required Guid ClientOperationId { get; init; }

    [JsonRequired]
    public required int ExpectedOrderVersion { get; init; }

    public long? ExpectedAccountRevision { get; init; }
    public required string Currency { get; init; }
    public IReadOnlyList<ManualRefundSelectionRequest> ManualRefunds { get; init; } = [];
}

public sealed record ManualRefundSelectionRequest
{
    [JsonRequired]
    public required Guid PaymentId { get; init; }

    [JsonRequired]
    public required long AmountMinor { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderAmendmentResolutionStartRequest
{
    public required OrderAmendmentResolutionQuoteRequest Quote { get; init; }
    public required string QuoteHash { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualTillConfirmationRequest
{
    [JsonRequired]
    public required Guid PaymentId { get; init; }

    [JsonRequired]
    public required string TillReference { get; init; }
}
