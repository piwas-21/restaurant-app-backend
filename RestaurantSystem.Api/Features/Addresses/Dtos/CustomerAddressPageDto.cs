namespace RestaurantSystem.Api.Features.Addresses.Dtos;

public sealed record CustomerAddressPageDto(int SchemaVersion, IReadOnlyList<CustomerAddressDto> Items, Guid? NextAfterId);
