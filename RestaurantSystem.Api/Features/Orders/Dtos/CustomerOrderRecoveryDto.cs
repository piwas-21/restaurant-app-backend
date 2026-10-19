using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerOrderRecoveryDto
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public Guid OperationId { get; init; }
    [JsonRequired]
    public string PayloadHash { get; init; } = string.Empty;
}
