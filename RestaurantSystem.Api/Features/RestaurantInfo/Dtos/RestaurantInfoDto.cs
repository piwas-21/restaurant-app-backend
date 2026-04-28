namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

public record RestaurantInfoDto(
    Guid Id,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode,
    string Country,
    decimal? Latitude,
    decimal? Longitude,
    string Email,
    string? Website,
    IReadOnlyList<RestaurantPhoneNumberDto> PhoneNumbers);
