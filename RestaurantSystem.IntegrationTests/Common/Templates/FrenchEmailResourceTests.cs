using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// French, the first language GAP-2 actually translates (EMAIL-LOCALISATION-PLAN §5 S7) — and the
/// first slice of the gap a guest can see: everything before it resolved a culture that rendered the
/// same English bytes.
/// </summary>
/// <remarks>
/// <para>
/// The three risks a translation carries are all silent, so all three are pinned here rather than
/// eyeballed. A <b>missing key</b> falls back to English mid-sentence, giving a French mail an
/// English line nobody notices in review. A <b>mangled placeholder</b> — <c>{0}</c> retyped, or a
/// stray brace in French prose — makes <c>string.Format</c> throw inside a send that half the app
/// swallows, so the mail simply never arrives. And <b>markup in a value</b> lands unescaped in the
/// HTML body, because the template's own markup is deliberately not encoded.
/// </para>
/// <para>
/// Written against the COMPILED satellite assembly (<c>GetResourceSet(..., tryParents: false)</c>),
/// not the <c>.resx</c> files on disk: a file that is not embedded, or a culture folder that never
/// ships, is exactly the failure that would otherwise pass a file-based check and still send English.
/// S8+ generalise this over a list of locales; with one translated language the list is one entry.
/// </para>
/// </remarks>
public partial class FrenchEmailResourceTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    /// <summary>Every numbered placeholder in a value, e.g. <c>{0}</c>. Nothing else may use braces.</summary>
    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");

    public static TheoryData<string> Sets()
    {
        var data = new TheoryData<string>();

        foreach (var set in EmailResourceSets())
        {
            data.Add(set);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void Every_english_key_is_translated_and_no_extra_key_is_invented(string set)
    {
        var english = Keys(set, CultureInfo.InvariantCulture);
        var french = Keys(set, French);

        french.Should().BeEquivalentTo(english,
            "a key missing from the French set renders that ONE line in English, and an extra key is "
            + "a translation of something nothing reads");
    }

    /// <summary>
    /// The placeholders must be the same SET, argument for argument. Order may legitimately differ —
    /// French word order is not English word order — but a dropped <c>{1}</c> loses a value from the
    /// mail and an invented <c>{2}</c> throws <c>FormatException</c> at send time.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sets))]
    public void Every_translation_carries_exactly_the_placeholders_the_english_does(string set)
    {
        var french = Values(set, French);

        foreach (var (key, english) in Values(set, CultureInfo.InvariantCulture))
        {
            french.TryGetValue(key, out var translated).Should().BeTrue(
                "{0}.{1} must exist in French — the parity test says which key, this one says why it matters",
                set, key);

            Indexes(translated!).Should().BeEquivalentTo(Indexes(english),
                "{0}.{1} must format with the arguments its caller passes", set, key);
        }
    }

    /// <summary>
    /// A resource value is TEXT. The templates interpolate it into HTML without encoding it — that is
    /// what lets the English copy carry the mail's own markup — so a stray tag or brace in a
    /// translation is either injected markup or a format crash.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sets))]
    public void No_translation_smuggles_markup_or_a_stray_brace(string set)
    {
        foreach (var (key, french) in Values(set, French))
        {
            french.Should().NotContain("<", "{0}.{1} is text, and the template owns the markup", set, key);
            french.Should().NotContain("&", "{0}.{1} would emit a bare ampersand into the HTML body (§6.3)", set, key);
            Placeholder().Replace(french, string.Empty).Should().NotContainAny("{", "}",
                "a brace outside a numbered placeholder makes string.Format throw at send time");
        }
    }

    /// <summary>
    /// The end-to-end claim, and the one that would still fail with every unit above green: a French
    /// order receipt with an English line left in it. Asserted on the MARKER words of the English
    /// copy rather than on a French snapshot, so it keeps working when the wording is polished.
    /// </summary>
    [Fact]
    public void A_french_order_receipt_contains_no_english_copy()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            French, Brand, "Jane Doe", "ORD-1", "DineIn", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "admin@demo.test");

        html.Should().Contain("Commande reçue").And.Contain("Merci pour votre commande");
        html.Should().NotContainAny(
            "Order Received", "Order Items", "Total Amount", "Pending Confirmation",
            "Thank you for your order", "Best regards", "automated message", "All rights reserved");
    }

    /// <summary>
    /// The subject line is the half a guest sees before opening anything, and it is rendered by a
    /// different method than the body — a set could be translated and its subject still English.
    /// </summary>
    [Fact]
    public void The_french_subjects_are_translated_too()
    {
        EmailTemplates.OrderReceived.GetSubject(French, Brand).Should().StartWith("Commande reçue");
        EmailTemplates.ReservationConfirmation.GetSubject(French, Brand)
            .Should().StartWith("Confirmation de réservation");
        EmailTemplates.PasswordReset.GetSubject(French, Brand).Should().StartWith("Réinitialisez");
    }

    /// <summary>
    /// §6.2, as amended by S1 and still in force: a culture selects WORDING only. `EmailText.Format`
    /// runs under the invariant culture over pre-formatted strings, so no French mail may turn
    /// <c>12.50</c> into <c>12,50</c> — the currency is a per-tenant label, and the amount is the
    /// caller's string.
    /// </summary>
    [Fact]
    public void French_does_not_reformat_the_money()
    {
        var ambient = CultureInfo.CurrentCulture;

        try
        {
            // Ambient is pinned, exactly as the golden suite pins it: §6.2 (amended by S1) says
            // amounts keep their AMBIENT formatting and the culture argument selects wording only.
            // Without this the assertion would read the developer's own locale — on a machine set to
            // French Switzerland it renders "CHF 25,00" with the ENGLISH culture too, which is the
            // decision working, not failing.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var html = EmailTemplates.OrderReceived.GetHtmlBody(
                French, Brand, "Jane Doe", "ORD-1", "DineIn", 25.00m, "CHF",
                [("Burger", 2, 12.50m)], "admin@demo.test");

            html.Should().Contain("CHF 25.00").And.NotContain("CHF 25,00",
                "a French mail says CHF, not EUR, and formats the amount the way the tenant's server "
                + "does — culture-formatted money is a separate decision (§6.2)");
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }


    /// <summary>
    /// The TEXT body, which the marker test above does not reach — and which is where the label
    /// punctuation lives. A French mail writes a space before its colon; nine of these lines used to
    /// glue it on in C#, so the resources were spaced correctly and the rendered mail was not.
    /// </summary>
    [Fact]
    public void The_french_text_bodies_punctuate_their_labels_the_way_french_does()
    {
        var receipt = EmailTemplates.OrderReceived.GetTextBody(
            French, Brand, "Jane Doe", "ORD-1", "DineIn", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "admin@demo.test");

        receipt.Should().Contain("NUMÉRO DE COMMANDE : ORD-1").And.NotContain("COMMANDE: ");
        receipt.Should().NotContainAny("Order Received", "Order Items", "Best regards");
    }

    /// <summary>
    /// A date is not a string in the resources, so every assertion above passes on a French mail
    /// that says "Friday, 21 August 2026". The ambient culture is pinned to invariant because that is
    /// what the container runs as — the point is that the CULTURE ARGUMENT is what decides now.
    /// </summary>
    [Fact]
    public void A_french_mail_writes_its_dates_in_french()
    {
        var ambient = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var body = EmailTemplates.ReservationConfirmation.GetTextBody(
                French, Brand, "Jane Doe", "T12", new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
                new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 2, "admin@demo.test");

            body.Should().Contain("vendredi 21 août 2026");
            body.Should().NotContainAny("Friday", "August", "Monday", "Tuesday", "Wednesday", "Thursday");
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    /// <summary>
    /// An order can have no customer name — a QR or counter order nobody typed one into — and the
    /// three order senders used to substitute the literal "Valued Customer", which a French guest
    /// read verbatim. The fixture passes what production now passes: nothing.
    /// </summary>
    [Fact]
    public void A_nameless_guest_is_greeted_in_french_rather_than_in_english()
    {
        var receipt = EmailTemplates.OrderReceived.GetTextBody(
            French, Brand, string.Empty, "ORD-1", "DineIn", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "admin@demo.test");

        receipt.Should().Contain("Bonjour,").And.NotContain("Valued Customer");
    }

    /// <summary>
    /// A specific culture must reach its parent's resources: <c>fr-CH</c> is French. Production
    /// normalises to the primary subtag before it gets here, so this pins the layer BELOW that —
    /// the dev-only test controller and any future caller hand over whatever they like.
    /// </summary>
    [Fact]
    public void A_regional_french_culture_resolves_to_the_french_resources()
    {
        EmailText.For(CultureInfo.GetCultureInfo("fr-CH"), "OrderReceived")["Heading"]
            .Should().Be("Commande reçue");
    }

    private static IEnumerable<string> EmailResourceSets() =>
        typeof(EmailText).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("RestaurantSystem.Api.Resources.Email.", StringComparison.Ordinal)
                && name.EndsWith(".resources", StringComparison.Ordinal))
            .Select(name => name["RestaurantSystem.Api.Resources.Email.".Length..^".resources".Length])
            .OrderBy(name => name, StringComparer.Ordinal);

    private static Dictionary<string, string> Values(string set, CultureInfo culture)
    {
        var manager = new ResourceManager(
            "RestaurantSystem.Api.Resources.Email." + set, typeof(EmailText).Assembly);

        // tryParents: false — the whole point is to see THIS culture's own set. With parents on, a
        // French satellite that never shipped would answer with the English one and every assertion
        // in this class would pass on it.
        var resources = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        resources.Should().NotBeNull(
            "the {0} resource set must exist for culture '{1}' — check the .resx is embedded", set, culture.Name);

        return resources!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    private static Dictionary<string, string>.KeyCollection Keys(string set, CultureInfo culture) =>
        Values(set, culture).Keys;

    private static IEnumerable<int> Indexes(string value) =>
        Placeholder().Matches(value).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(index => index);
}
