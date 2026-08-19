using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// German, the second Swiss market language and the second translation GAP-2 ships
/// (EMAIL-LOCALISATION-PLAN §5 S8). The structural claims run for it automatically through
/// <see cref="TranslatedEmailResourceTests"/>; what is pinned here is what only German can say.
/// </summary>
/// <remarks>
/// The copy is written in SWISS German orthography — "Grüsse", not the eszett — because the only
/// tenant this reaches is in Geneva and the next likely one is Swiss too. That is a wording
/// decision, not a technical one, and it is recorded here so a later translator does not "fix" it
/// into a German-German mail by accident.
/// </remarks>
public class GermanEmailResourceTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    private static EmailBranding Brand => EmailResources.Brand;

    /// <summary>
    /// The end-to-end claim: a German order receipt with an English line left in it. Asserted on the
    /// MARKER words of the English copy rather than on a German snapshot, so it keeps working when
    /// the wording is polished.
    /// </summary>
    [Fact]
    public void A_german_order_receipt_contains_no_english_copy()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            German, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, [("Burger", 2, 12.50m)], "CHF"), "admin@demo.test");

        html.Should().Contain("Bestellung eingegangen").And.Contain("Vielen Dank für Ihre Bestellung");
        html.Should().NotContainAny(
            "Order Received", "Order Items", "Total Amount", "Pending Confirmation",
            "Thank you for your order", "Best regards", "automated message", "All rights reserved");
    }

    /// <summary>
    /// A German mail is not a French one. Every assertion in this file except this one would still
    /// pass if <c>*.de.resx</c> had been copied from <c>*.fr.resx</c> for the sets it does not
    /// render, so the receipt is checked against French marker words too.
    /// </summary>
    [Fact]
    public void A_german_order_receipt_is_not_the_french_one()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            German, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, [("Burger", 2, 12.50m)], "CHF"), "admin@demo.test");

        html.Should().NotContainAny("Commande", "Merci", "Cordialement", "Articles commandés");
    }

    /// <summary>
    /// The subject line is the half a guest sees before opening anything, and it is rendered by a
    /// different method than the body — a set could be translated and its subject still English.
    /// </summary>
    [Fact]
    public void The_german_subjects_are_translated_too()
    {
        EmailTemplates.OrderReceived.GetSubject(German, Brand).Should().StartWith("Bestellung eingegangen");
        EmailTemplates.ReservationConfirmation.GetSubject(German, Brand)
            .Should().StartWith("Reservierungsbestätigung");
        EmailTemplates.PasswordReset.GetSubject(German, Brand).Should().StartWith("Setzen Sie Ihr Passwort zurück");
    }

    /// <summary>
    /// The reason the colon is a RESOURCE rather than a rule in code (S7): French writes a space
    /// before it and German writes it exactly as English does. The two languages have to be able to
    /// disagree without either of them touching a template.
    /// </summary>
    [Fact]
    public void German_writes_its_label_colon_the_english_way()
    {
        EmailText.For(German, EmailText.CommonSet)["LabelColon"].Should().Be(":");

        var receipt = EmailTemplates.OrderReceived.GetTextBody(
            German, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, [("Burger", 2, 12.50m)], "CHF"), "admin@demo.test");

        receipt.Should().Contain("BESTELLNUMMER: ORD-1").And.NotContain("BESTELLNUMMER : ");
        receipt.Should().NotContainAny("Order Received", "Order Items", "Best regards");
    }

    /// <summary>
    /// A date is not a string in the resources, so every assertion above passes on a German mail
    /// that says "Friday, 21 August 2026". The ambient culture is pinned to invariant because that is
    /// what the container runs as — the point is that the CULTURE ARGUMENT is what decides.
    /// </summary>
    [Fact]
    public void A_german_mail_writes_its_dates_in_german()
    {
        var ambient = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var body = EmailTemplates.ReservationConfirmation.GetTextBody(
            German, Brand, "Jane Doe", new ReservationMailDetails(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 2, "T12", null), "admin@demo.test");

            body.Should().Contain("Freitag, 21. August 2026");
            body.Should().NotContainAny("Friday", "vendredi", "Monday", "Tuesday", "Wednesday", "Thursday");
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    /// <summary>
    /// §6.2: a culture selects WORDING only. German formats money with a comma, so a German mail is
    /// the second chance for a culture to leak into an amount the caller already rendered.
    /// </summary>
    [Fact]
    public void German_does_not_reformat_the_money()
    {
        var ambient = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var html = EmailTemplates.OrderReceived.GetHtmlBody(
            German, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, [("Burger", 2, 12.50m)], "CHF"), "admin@demo.test");

            html.Should().Contain("CHF 25.00").And.NotContain("CHF 25,00",
                "the amount is the caller's pre-formatted string — culture-formatted money is a "
                + "separate decision (§6.2)");
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    /// <summary>
    /// An order can have no customer name — a QR or counter order nobody typed one into. The
    /// fixture passes what production passes: nothing.
    /// </summary>
    [Fact]
    public void A_nameless_guest_is_greeted_in_german_rather_than_in_english()
    {
        var receipt = EmailTemplates.OrderReceived.GetTextBody(
            German, Brand, string.Empty, new OrderMailDetails("ORD-1", "DineIn", 25.00m, [("Burger", 2, 12.50m)], "CHF"), "admin@demo.test");

        receipt.Should().Contain("Guten Tag,").And.NotContain("Valued Customer");
    }

    /// <summary>
    /// A specific culture must reach its parent's resources: <c>de-CH</c> is German. Production
    /// normalises to the primary subtag before it gets here, so this pins the layer BELOW that.
    /// </summary>
    [Fact]
    public void A_regional_german_culture_resolves_to_the_german_resources()
    {
        EmailText.For(CultureInfo.GetCultureInfo("de-CH"), "OrderReceived")["Heading"]
            .Should().Be("Bestellung eingegangen");
    }
}
