using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Cultures used when rendering mail. Every send path passes one explicitly
/// (EMAIL-LOCALISATION-PLAN §3) — none is read from the ambient culture.
/// </summary>
public static class EmailCultures
{
    /// <summary>
    /// English: the neutral resource set and the terminal fallback of the §1 resolution
    /// chain. Until the language resolver lands (slice S3/S5) every caller passes this,
    /// so output is unchanged.
    /// </summary>
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
}
