using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerOrderCancellationGuestDto
{
    [JsonRequired]
    public required CustomerOrderRecoveryDto Proof { get; init; }
    [JsonRequired]
    public required CustomerOrderCancellationIntentDto Intent { get; init; }
}
