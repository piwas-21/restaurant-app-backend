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
