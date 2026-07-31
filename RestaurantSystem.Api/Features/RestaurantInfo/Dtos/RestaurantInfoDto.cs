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
    string? ThemePaletteKey,
    string? LogoUrl,
    string? LogoDarkUrl,
    IReadOnlyList<RestaurantPhoneNumberDto> PhoneNumbers);

/// <summary>Which of the two stored logos an upload or delete is addressing.</summary>
/// <remarks>
/// Bound from the route (<c>/api/restaurant-info/logo/{variant}</c>), so an unknown value is a
/// 400 from <c>[ApiController]</c>'s model-binding before any handler runs. An enum rather than a
/// <c>bool isDark</c> because the URL then says which one it means.
/// </remarks>
public enum LogoVariant
{
    Light,
    Dark,
}
