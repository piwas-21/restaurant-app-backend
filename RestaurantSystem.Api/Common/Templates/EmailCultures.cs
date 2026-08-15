using System.Globalization;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Cultures used when rendering mail. Every send path passes one explicitly
/// (EMAIL-LOCALISATION-PLAN §3) — none is read from the ambient culture.
/// </summary>
public static class EmailCultures
{
    /// <summary>
    /// English: the neutral resource set and the terminal fallback of the §1 resolution
    /// chain (<c>LanguageCode.Fallback</c>). Since S5 only the dev-only diagnostic endpoints
    /// name it directly; every production send passes what the resolver returned.
    /// </summary>
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo(LanguageCode.Fallback);

    /// <summary>
    /// The culture to render a mail in for a resolved language code — the one conversion from
    /// the resolver's string answer to the templates' <see cref="CultureInfo"/>.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="LanguageCode.Normalize"/> rather than trusting the caller, so an
    /// unknown, malformed or region-qualified value renders English instead of throwing
    /// <see cref="CultureNotFoundException"/> inside a mail send that half the app swallows. There
    /// is no ambient read here and no default argument: a caller with nothing to pass has not
    /// resolved a language, and <see cref="English"/> is a decision it should have to write down.
    /// </remarks>
    public static CultureInfo For(string? language) =>
        LanguageCode.Normalize(language) is { } code ? CultureInfo.GetCultureInfo(code) : English;
}
