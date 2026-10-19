using System.Text.Json.Serialization;
namespace RestaurantSystem.Api.Features.Addresses.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerAddressInput : AddressTextContent;
