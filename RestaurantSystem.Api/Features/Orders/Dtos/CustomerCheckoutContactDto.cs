using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerCheckoutContactDto
{
    [JsonRequired]
    public string Name { get; init; } = string.Empty;
    [JsonRequired]
    public string Email { get; init; } = string.Empty;
    [JsonRequired]
    public string Phone { get; init; } = string.Empty;
}
