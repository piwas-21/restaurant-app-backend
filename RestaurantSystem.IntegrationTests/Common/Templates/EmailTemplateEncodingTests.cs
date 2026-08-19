using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Guest-supplied values are HTML-encoded in the HTML bodies (EMAIL-LOCALISATION-PLAN §6.3).
/// <para>
/// Until this slice they were interpolated raw, so anything a guest typed into a delivery
/// note, a special request or their own name became live markup in the message the operator
/// opens — in the admin order alert, the one mail that is read on a phone in a kitchen.
/// The plain-text bodies are deliberately NOT encoded: there is no markup to escape there,
/// and encoding would show the guest's own note back as <c>&amp;lt;</c> noise.
/// </para>
/// </summary>
public class EmailTemplateEncodingTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly EmailLinks Links = new("https://api.demo.test", "https://demo.test", "admin@demo.test");
    private static readonly (string name, int quantity, decimal price)[] Items = [("Burger", 2, 12.50m)];

    private const string Payload = "<script>alert('xss')</script>";
    private const string Encoded = "&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;";

    [Fact]
    public void Order_received_encodes_the_special_instructions()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "Delivery", 25.00m, "CHF", Items, SpecialInstructions: Payload, DeliveryAddress: Payload), "admin@demo.test");

        html.Should().Contain(Encoded).And.NotContain(Payload);
    }

    /// <summary>
    /// <see cref="System.Net.WebUtility.HtmlEncode"/> also escapes non-ASCII, so an accented guest
    /// name becomes a numeric entity. That is intended and renders identically in a mail client —
    /// pinned here because the golden fixtures are ASCII-only and would never show it, and because
    /// the obvious "fix" (switching to a smarter encoder) is a change nobody would otherwise catch.
    /// The plain-text body keeps the real characters.
    /// </summary>
    [Fact]
    public void An_accented_name_is_escaped_in_html_and_left_alone_in_text()
    {
        const string Name = "Zoë Müller";

        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, Name, new OrderMailDetails("ORD-1", "Delivery", 25.00m, "CHF", Items, SpecialInstructions: null, DeliveryAddress: null), "admin@demo.test");
        var text = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, Name, new OrderMailDetails("ORD-1", "Delivery", 25.00m, "CHF", Items, SpecialInstructions: null), "admin@demo.test");

        html.Should().Contain("Zo&#235; M&#252;ller").And.NotContain(Name);
        text.Should().Contain(Name);
    }

    /// <summary>
    /// Item names are typed by the restaurant, not the guest, so this is not the injection path the
    /// slice was written for — but "Fish &amp; Chips" is an ordinary menu item and it reached both
    /// the guest receipt and the kitchen alert as a bare ampersand.
    /// </summary>
    [Fact]
    public void A_menu_item_name_is_encoded_in_the_html_tables()
    {
        (string name, int quantity, decimal price)[] items = [("Fish & Chips <b>", 1, 9.00m)];

        var receipt = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "Delivery", 9.00m, "CHF", items, SpecialInstructions: null, DeliveryAddress: null), "admin@demo.test");
        var alert = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            EmailCultures.English, Brand,
            new EmailGuest("Jane Doe", "jane@demo.test", "+41000000"),
            new OrderMailDetails("ORD-1", "Delivery", 9.00m, "CHF", items, "token"),
            Links);

        receipt.Should().Contain("Fish &amp; Chips &lt;b&gt;").And.NotContain("Fish & Chips <b>");
        alert.Should().Contain("Fish &amp; Chips &lt;b&gt;").And.NotContain("Fish & Chips <b>");
    }

    [Fact]
    public void Order_received_leaves_the_plain_text_body_unencoded()
    {
        var text = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "Delivery", 25.00m, "CHF", Items, SpecialInstructions: Payload), "admin@demo.test");

        text.Should().Contain(Payload).And.NotContain(Encoded);
    }

    [Fact]
    public void Admin_order_alert_encodes_the_customer_identity_and_the_notes()
    {
        var html = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            EmailCultures.English, Brand,
            new EmailGuest(Payload, Payload, Payload),
            new OrderMailDetails("ORD-1", "Delivery", 25.00m, "CHF", Items, "token", Payload, Payload),
            Links);

        html.Should().NotContain(Payload);
        html.Should().Contain(Encoded);
    }

    [Fact]
    public void Reservation_mails_encode_the_guest_supplied_fields()
    {
        var date = new DateTime(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc);

        var confirmation = EmailTemplates.ReservationConfirmation.GetHtmlBody(
            EmailCultures.English, Brand, Payload, new ReservationMailDetails(date, new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", Payload), "admin@demo.test");
        var approved = EmailTemplates.ReservationApproved.GetHtmlBody(
            EmailCultures.English, Brand, Payload, new ReservationMailDetails(date, new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", Payload), "admin@demo.test", Payload);
        var adminNotification = EmailTemplates.ReservationAdminNotification.GetHtmlBody(
            EmailCultures.English, Brand,
            new EmailGuest(Payload, Payload, Payload),
            new ReservationMailDetails(
                date, new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", Payload),
            Links);

        confirmation.Should().NotContain(Payload).And.Contain(Encoded);
        approved.Should().NotContain(Payload).And.Contain(Encoded);
        adminNotification.Should().NotContain(Payload).And.Contain(Encoded);
    }

    [Fact]
    public void Membership_confirmation_encodes_the_group_and_member_names()
    {
        var html = EmailTemplates.MembershipConfirmation.GetHtmlBody(
            EmailCultures.English, Brand, Payload, Payload, Payload);

        html.Should().NotContain(Payload).And.Contain(Encoded);
    }
}
