using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerCheckoutAddressDto
{
    [JsonRequired]
    public string Street { get; init; } = string.Empty;
    [JsonRequired]
    public string City { get; init; } = string.Empty;
    [JsonRequired]
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string? AdditionalInfo { get; init; }
}
