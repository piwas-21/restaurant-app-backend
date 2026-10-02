using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelDecisionReport
{
    [JsonRequired]
    public Guid LeaseId { get; init; }
    public string State { get; init; } = string.Empty;
    public string CanonicalState { get; init; } = string.Empty;
    public string CanonicalHash { get; init; } = string.Empty;
    [JsonRequired]
    public DateTimeOffset ObservedAt { get; init; }
}
