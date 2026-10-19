using System.Text.Json.Serialization;
namespace RestaurantSystem.Api.Features.Addresses.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerAddressWriteRequest([property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] CustomerAddressInput Address);
