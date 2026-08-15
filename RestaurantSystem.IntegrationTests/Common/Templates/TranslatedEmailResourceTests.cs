using System.Globalization;
using FluentAssertions;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The three structural claims that hold for EVERY translated language
/// (EMAIL-LOCALISATION-PLAN §5 S8) — run over <see cref="EmailResources.Translated"/> × every
/// resource set, so a new locale gets them by adding one entry to that list.
/// </summary>
/// <remarks>
/// The three risks a translation carries are all silent, so all three are pinned here rather than
/// eyeballed. A <b>missing key</b> falls back to English mid-sentence, giving a translated mail an
/// English line nobody notices in review. A <b>mangled placeholder</b> — <c>{0}</c> retyped, or a
/// stray brace in prose — makes <c>string.Format</c> throw inside a send that half the app
/// swallows, so the mail simply never arrives. And <b>markup in a value</b> lands unescaped in the
/// HTML body, because the template's own markup is deliberately not encoded.
/// <para>
/// What they cannot see is whether the words are the right words: that a set is French rather than
/// Italian, that a label is punctuated the way the language punctuates it, or that a date reads in
/// the recipient's language. Those live in the per-language suites beside this file.
/// </para>
/// </remarks>
public class TranslatedEmailResourceTests
{
    public static TheoryData<string, string> Translations()
    {
        var data = new TheoryData<string, string>();

        foreach (var culture in EmailResources.Translated)
        {
            foreach (var set in EmailResources.Sets())
            {
                data.Add(culture.Name, set);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_english_key_is_translated_and_no_extra_key_is_invented(string language, string set)
    {
        var english = EmailResources.Keys(set, CultureInfo.InvariantCulture);
        var translated = EmailResources.Keys(set, CultureInfo.GetCultureInfo(language));

        translated.Should().BeEquivalentTo(english,
            "a key missing from the {0} set renders that ONE line in English, and an extra key is "
            + "a translation of something nothing reads", language);
    }

    /// <summary>
    /// The placeholders must be the same SET, argument for argument. Order may legitimately differ —
    /// neither French nor German word order is English word order — but a dropped <c>{1}</c> loses a
    /// value from the mail and an invented <c>{2}</c> throws <c>FormatException</c> at send time.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_translation_carries_exactly_the_placeholders_the_english_does(string language, string set)
    {
        var translations = EmailResources.Values(set, CultureInfo.GetCultureInfo(language));

        foreach (var (key, english) in EmailResources.Values(set, CultureInfo.InvariantCulture))
        {
            translations.TryGetValue(key, out var translated).Should().BeTrue(
                "{0}.{1} must exist in {2} — the parity test says which key, this one says why it matters",
                set, key, language);

            EmailResources.Indexes(translated!).Should().BeEquivalentTo(EmailResources.Indexes(english),
                "{0}.{1} must format with the arguments its caller passes", set, key);
        }
    }

    /// <summary>
    /// A resource value is TEXT. The templates interpolate it into HTML without encoding it — that is
    /// what lets the English copy carry the mail's own markup — so a stray tag or brace in a
    /// translation is either injected markup or a format crash.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void No_translation_smuggles_markup_or_a_stray_brace(string language, string set)
    {
        foreach (var (key, value) in EmailResources.Values(set, CultureInfo.GetCultureInfo(language)))
        {
            value.Should().NotContain("<", "{0}.{1} is text, and the template owns the markup", set, key);
            value.Should().NotContain("&", "{0}.{1} would emit a bare ampersand into the HTML body (§6.3)", set, key);
            EmailResources.Placeholder().Replace(value, string.Empty).Should().NotContainAny("{", "}",
                "a brace outside a numbered placeholder makes string.Format throw at send time");
        }
    }
}
