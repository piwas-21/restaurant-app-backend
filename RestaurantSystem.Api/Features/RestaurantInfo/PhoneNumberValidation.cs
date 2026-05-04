using System.Text.RegularExpressions;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// E.164 phone number validation for the restaurant-info commands.
///
/// Note: this lives as a static helper because FluentValidation isn't
/// wired into <c>CustomMediator</c> today (registered in DI but never
/// invoked — see backend issue #9). When that pipeline lands, this
/// can move to an FV rule. Until then, handlers call <see cref="EnsureValid"/>
/// up-front and throw <c>BadRequestException</c> on a malformed input.
/// </summary>
internal static class PhoneNumberValidation
{
    // E.164: leading '+', then 7–15 digits. First digit 1-9 (no leading 0).
    // The spec allows 1–15, but a real phone needs subscriber digits beyond
    // the country code; tightening the floor to 7 keeps "+41" (country-only)
    // out without invalidating realistic numbers.
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    public static bool IsValid(string number) => !string.IsNullOrWhiteSpace(number) && E164.IsMatch(number);
}
