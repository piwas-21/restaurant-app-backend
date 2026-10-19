namespace RestaurantSystem.Api.Features.Addresses.Dtos;

public sealed record CustomerAddressDto(Guid Id, string Label, string AddressLine1, string? AddressLine2,
    string City, string? State, string PostalCode, string Country, string? Phone, bool IsDefault,
    string? DeliveryInstructions);
