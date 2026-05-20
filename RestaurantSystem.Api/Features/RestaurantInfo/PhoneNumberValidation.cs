using System.Text.RegularExpressions;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// E.164 phone number validation for the restaurant-info commands.
///
/// FluentValidation is wired (<c>CustomMediator</c> resolves
/// <c>IValidator&lt;TCommand&gt;</c> and runs it on every dispatch).
/// This static helper exists so the regex can be shared across
/// multiple commands without duplicating it in each FV validator.
/// Today it's called directly from <c>AddPhoneNumberCommand</c> and
/// <c>UpdatePhoneNumberCommand</c> handlers via <see cref="IsValid"/>;
/// they throw <c>BadRequestException</c> when it returns false.
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
