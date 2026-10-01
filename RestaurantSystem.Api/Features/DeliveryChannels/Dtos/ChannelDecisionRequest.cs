using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelDecisionRequest
{
    [JsonRequired]
    public Guid OperationId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    [JsonRequired]
    public int ExpectedVersion { get; init; }
}
