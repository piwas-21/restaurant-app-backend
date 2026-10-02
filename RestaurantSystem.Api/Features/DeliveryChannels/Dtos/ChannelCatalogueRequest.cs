using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelCatalogueRequest
{
    public string Provider { get; init; } = string.Empty;
    public string StoreId { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    [JsonRequired]
    public bool IsSandbox { get; init; }
    public string Language { get; init; } = "en";
    public List<ChannelAvailabilitySelection> Items { get; init; } = [];
}
