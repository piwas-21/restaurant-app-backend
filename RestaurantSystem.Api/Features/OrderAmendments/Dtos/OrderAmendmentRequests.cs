using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentQuoteRequest
{
    [JsonRequired]
    public int ExpectedOrderVersion { get; init; }
    public long? ExpectedAccountRevision { get; init; }
    public string? Reason { get; init; }
    [JsonRequired]
    public bool ReviewAcknowledged { get; init; }
    [JsonRequired]
    public bool PreparingOverrideAcknowledged { get; init; }
    [JsonRequired]
    public bool ReleaseAdditionsToKitchen { get; init; }
    [JsonRequired]
    public bool LocalProviderSupplementConsent { get; init; }
    public string? ProviderConsentNote { get; init; }
    public int? PointsToRedeem { get; init; }
    public List<CreateOrderItemDto> Additions { get; init; } = [];
    public List<OrderAmendmentLineChangeRequest> Changes { get; init; } = [];
}

public sealed record OrderAmendmentLineChangeRequest
{
    [JsonRequired]
    public Guid OrderItemId { get; init; }
    [JsonRequired]
    public OrderAmendmentChangeKind Kind { get; init; }

    /// <summary>One-based start ordinal for Void and Replace; never expanded into one row per unit.</summary>
    public int StartOrdinal { get; init; }
    public int Quantity { get; init; }

    /// <summary>New line for Replace, or the full same-identity line for InstructionChange.</summary>
    public CreateOrderItemDto? Current { get; init; }
}

public sealed record OrderAmendmentCommitRequest
{
    [JsonRequired]
    public Guid AmendmentId { get; init; }
    [JsonRequired]
    public Guid ClientOperationId { get; init; }
    [JsonRequired]
    public int ExpectedOrderVersion { get; init; }
    public long? ExpectedAccountRevision { get; init; }
    [JsonRequired]
    public bool ReviewAcknowledged { get; init; }
}
