using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Reads the email resource sets the way production does — through the COMPILED satellite
/// assembly — for the language suites in this folder (EMAIL-LOCALISATION-PLAN §5 S7/S8).
/// </summary>
/// <remarks>
/// <para>
/// Every lookup here uses <c>GetResourceSet(..., tryParents: false)</c>, and that is the whole
/// point of the file: with parents on, a satellite that never got embedded answers with the
/// English one, every assertion in every language suite passes, and the app sends English. A
/// file-based check over <c>*.resx</c> has exactly the same blind spot.
/// </para>
/// <para>
/// S8 generalised this out of <c>FrenchEmailResourceTests</c>, which owned it when there was one
/// translated language. Adding a locale is now one entry in <see cref="Translated"/> plus a suite
/// of rendering facts in that language — the structural three (key parity, placeholder parity, no
/// markup) come for free.
/// </para>
/// </remarks>
internal static partial class EmailResources
{
    /// <summary>
    /// The languages GAP-2 has actually translated. NOT the tenant's supported list: a language
    /// with no <c>.resx</c> set is a legitimate configuration (it falls back to English), while a
    /// language listed here and missing a key is a half-English mail.
    /// </summary>
    internal static readonly CultureInfo[] Translated =
    [
        CultureInfo.GetCultureInfo("fr"),
        CultureInfo.GetCultureInfo("de")
    ];

    /// <summary>Every numbered placeholder in a value, e.g. <c>{0}</c>. Nothing else may use braces.</summary>
    [GeneratedRegex(@"\{(\d+)\}")]
    internal static partial Regex Placeholder();

    /// <summary>The fixture brand every language suite renders against.</summary>
    internal static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");

    /// <summary>The resource set names discovered from the assembly's own manifest.</summary>
    internal static IEnumerable<string> Sets() =>
        typeof(EmailText).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("RestaurantSystem.Api.Resources.Email.", StringComparison.Ordinal)
                && name.EndsWith(".resources", StringComparison.Ordinal))
            .Select(name => name["RestaurantSystem.Api.Resources.Email.".Length..^".resources".Length])
            .OrderBy(name => name, StringComparer.Ordinal);

    internal static Dictionary<string, string> Values(string set, CultureInfo culture)
    {
        var manager = new ResourceManager(
            "RestaurantSystem.Api.Resources.Email." + set, typeof(EmailText).Assembly);

        // tryParents: false — see the type remarks.
        var resources = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        resources.Should().NotBeNull(
            "the {0} resource set must exist for culture '{1}' — check the .resx is embedded", set, culture.Name);

        return resources!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    internal static Dictionary<string, string>.KeyCollection Keys(string set, CultureInfo culture) =>
        Values(set, culture).Keys;

    internal static IEnumerable<int> Indexes(string value) =>
        Placeholder().Matches(value)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(index => index);
}
