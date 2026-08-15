using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// French, the first language GAP-2 actually translates (EMAIL-LOCALISATION-PLAN §5 S7) — and the
/// first slice of the gap a guest can see: everything before it resolved a culture that rendered the
/// same English bytes.
/// </summary>
/// <remarks>
/// What is asserted here is what only French can say: that the words are French rather than merely
/// present, that a label carries the space before its colon French requires, and that a date reads
/// in French. The structural three — key parity, placeholder parity, no markup — moved to
/// <see cref="TranslatedEmailResourceTests"/> in S8 and now run for every translated language.
/// </remarks>
public class FrenchEmailResourceTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    private static EmailBranding Brand => EmailResources.Brand;

    /// <summary>
    /// The end-to-end claim, and the one that would still fail with every structural test green: a
    /// French order receipt with an English line left in it. Asserted on the MARKER words of the
    /// English copy rather than on a French snapshot, so it keeps working when the wording is
    /// polished.
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
}
