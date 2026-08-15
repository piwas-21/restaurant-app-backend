using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The whitelist every <c>PreferredLanguage</c> column is written through
/// (EMAIL-LOCALISATION-PLAN §2). Deliberately a pure unit test: this helper is the one thing in
/// the localisation chain that must behave identically on a request thread, in a detached order
/// task and inside a migration-less unit context.
/// </summary>
public class LanguageCodeTests
{
    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("FR-ch", "fr")]      // §2: the resource sets are per language, so the region goes
    [InlineData("fr_CH", "fr")]      // underscore form, seen from non-browser clients
    [InlineData("  de-DE  ", "de")]
    [InlineData("zh-Hans-CN", "zh")]
    public void A_well_formed_tag_reduces_to_its_primary_subtag(string input, string expected) =>
        LanguageCode.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("klingon")]
    [InlineData("xx")]
    [InlineData("*")]                // Accept-Language wildcard is not a language
    [InlineData("-fr")]              // empty primary subtag
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_the_product_has_no_copy_for_is_absent_rather_than_stored(string? input) =>
        LanguageCode.Normalize(input).Should().BeNull(
            "null means 'no preference recorded' and falls through to the next rank of §1");

    /// <summary>
    /// The set is the frontend's ten locales (<c>frontend/src/locales/*.json</c>). If a locale is
    /// added there and not here, a guest's own language silently stops being recordable.
    /// </summary>
    /// <summary>
    /// An <c>Accept-Language</c> header is a weighted LIST and must be refused whole, not
    /// half-understood. This is the trap: truncating at the first separator makes
    /// <c>"fr-CH,fr;q=0.9,en;q=0.8"</c> — the Chrome-shaped header — resolve to <c>fr</c> and look
    /// correct, while the equally common region-less <c>"fr,en;q=0.9"</c> resolves to NOTHING and
    /// silently drops a French guest to the tenant default. Picking between a list's entries needs
    /// to know what the tenant supports, so it belongs to IEmailLanguageResolver, not here.
    /// </summary>
    [Theory]
    [InlineData("fr-CH,fr;q=0.9,en;q=0.8")]
    [InlineData("fr,en;q=0.9")]
    [InlineData("en;q=0.9")]
    [InlineData("de-DE, en;q=0.5")]
    [InlineData("en US")]
    public void A_header_shaped_value_is_refused_whole_rather_than_half_understood(string header) =>
        LanguageCode.Normalize(header).Should().BeNull(
            "a list is not a language tag, and half-parsing one loses a guest's language silently");

    /// <summary>
    /// The invariant every later slice rests on: whatever comes in, what comes out is either
    /// nothing or one of ten known codes. That — not any caller's care — is why S5 can hand the
    /// result to <c>CultureInfo</c> and <c>ResourceManager.GetString</c> without sanitising it.
    /// </summary>
    [Theory]
    [InlineData("en\0x")]
    [InlineData("en--US")]
    [InlineData("fr-")]
    [InlineData("../../etc/passwd")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("en-US-x-lvariant-POSIX-and-a-very-long-tail")]
    public void The_output_is_always_null_or_a_supported_code(string hostile)
    {
        var result = LanguageCode.Normalize(hostile);

        (result is null || LanguageCode.Supported.Contains(result)).Should().BeTrue();
    }

    [Fact]
    public void A_pathologically_long_value_cannot_reach_the_column()
    {
        LanguageCode.Normalize(new string('a', 5_000)).Should().BeNull();
        LanguageCode.Normalize("fr" + new string('x', 5_000)).Should().BeNull();
        LanguageCode.Normalize("fr-" + new string('x', 5_000)).Should().Be("fr");
    }

    /// <summary>
    /// Casing is folded with the INVARIANT culture. Under <c>tr-TR</c>, <c>"IT".ToLower()</c> is
    /// <c>"ıt"</c> — so a switch to the culture-sensitive overload would leave every other test in
    /// this file green on an en-US runner and break Italian and Turkish on a Turkish-culture host.
    /// </summary>
    [Fact]
    public void Casing_is_folded_independently_of_the_ambient_culture()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            LanguageCode.Normalize("IT").Should().Be("it");
            LanguageCode.Normalize("TR").Should().Be("tr");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void The_supported_set_is_the_products_ten_locales() =>
        LanguageCode.Supported.Should().BeEquivalentTo(
            new[] { "ar", "de", "en", "es", "fr", "it", "nl", "ru", "tr", "zh" });

    [Fact]
    public void The_terminal_fallback_is_itself_supported() =>
        LanguageCode.Supported.Should().Contain(LanguageCode.Fallback);

    /// <summary>
    /// Every value this helper can return has to fit the column it is written to, or a legitimate
    /// language becomes a 500 on the write path.
    /// </summary>
    [Fact]
    public void Every_supported_code_fits_the_column() =>
        LanguageCode.Supported.Should().OnlyContain(code => code.Length <= LanguageCode.MaxLength);
}
