namespace RestaurantSystem.Api.Features.Addresses.Dtos;

public sealed record CustomerAddressActionResultDto(int SchemaVersion, Guid AddressId, string State);
