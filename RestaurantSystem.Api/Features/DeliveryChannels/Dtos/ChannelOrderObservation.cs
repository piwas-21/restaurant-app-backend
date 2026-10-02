using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelOrderObservation
{
    [JsonRequired] public required string Provider { get; init; }
    [JsonRequired] public required string StoreId { get; init; }
    [JsonRequired] public required string ExternalOrderId { get; init; }
    [JsonRequired] public required string CanonicalState { get; init; }
    [JsonRequired] public required string CanonicalHash { get; init; }
    [JsonRequired] public required DateTimeOffset ObservedAt { get; init; }
}

public sealed record ChannelOrderObservationDto(Guid OrderId, string CanonicalState, bool IsTerminal);
