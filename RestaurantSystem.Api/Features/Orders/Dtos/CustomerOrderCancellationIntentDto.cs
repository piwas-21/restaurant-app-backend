using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerOrderCancellationIntentDto
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public Guid OperationId { get; init; }
    [JsonRequired]
    public Guid OrderId { get; init; }
    [JsonRequired]
    public int ExpectedVersion { get; init; }
    [JsonRequired]
    public string PayloadHash { get; init; } = string.Empty;
}
