using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// Maps the <see cref="Domain.Entities.RestaurantInfo"/> singleton onto
/// <see cref="RestaurantInfoDto"/>. One place, because four handlers now return this shape
/// (read, update-info, update-logo, delete-logo) and a field added to the entity but forgotten
/// in one of them is invisible until a client notices the value is missing on one route only.
/// </summary>
public static class RestaurantInfoMapper
{
    /// <param name="baseUrl">
    /// Storage base URL (<c>AWS:S3:BaseUrl</c>). Unset for local storage, which already returns
    /// absolute URLs — <see cref="UrlJoin.Join"/> passes those through untouched.
    /// </param>
    public static RestaurantInfoDto ToDto(Domain.Entities.RestaurantInfo info, string? baseUrl) =>
        new(
            info.Id,
            info.Name,
            info.AddressLine1,
            info.AddressLine2,
            info.City,
            info.PostalCode,
            info.Country,
            info.Latitude,
            info.Longitude,
            info.Email,
            info.Website,
            info.ThemePaletteKey,
            ToAbsoluteUrl(baseUrl, info.LogoUrl),
            ToAbsoluteUrl(baseUrl, info.LogoDarkUrl),
            info.PhoneNumbers
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new RestaurantPhoneNumberDto(
                    p.Id, p.Label, p.Number, p.WhatsAppEnabled, p.DisplayOrder, p.IsActive))
                .ToList());

    /// <summary>
    /// Joins a stored logo path onto the storage base URL, collapsing "no logo" to null.
    /// </summary>
    /// <remarks>
    /// The null is load-bearing, not tidiness. <see cref="UrlJoin.Join"/> answers
    /// <see cref="string.Empty"/> for an absent path, and the clients render the logo as
    /// <c>logoUrl ?? &lt;the restaurant's name as text&gt;</c> — in JavaScript <c>??</c> does not
    /// fire on <c>""</c>, so an empty string would reach an <c>&lt;img&gt;</c> tag as its src and
    /// the header would show a broken image instead of the name.
    /// </remarks>
    private static string? ToAbsoluteUrl(string? baseUrl, string? storedUrl)
    {
        var joined = UrlJoin.Join(baseUrl, storedUrl);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }
}
