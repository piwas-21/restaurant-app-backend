using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

/// <summary>Normalized, authenticated gateway input. Marketplace credentials never enter this contract.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExternalOrderRequest
{
    public string Provider { get; init; } = string.Empty;
    public string StoreId { get; init; } = string.Empty;
    public string ExternalOrderId { get; init; } = string.Empty;
    public string DisplayId { get; init; } = string.Empty;
    public string CanonicalOrderHash { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    [JsonRequired]
    public decimal MerchantTotal { get; init; }
    public decimal? ReportedTax { get; init; }
    public DateTimeOffset PlacedAt { get; init; }
    public string FulfillmentType { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? Instructions { get; init; }
    public List<ExternalOrderItemRequest> Items { get; init; } = [];
}
