using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExternalOrderItemRequest
{
    public Guid ProductId { get; init; }
    public Guid? VariationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? VariationName { get; init; }
    public int Quantity { get; init; }
    [JsonRequired]
    public decimal UnitPrice { get; init; }
    [JsonRequired]
    public decimal Total { get; init; }
    public string? Instructions { get; init; }
}
