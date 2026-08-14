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
    private static readonly (string name, int quantity, decimal price)[] Items = [("Burger", 2, 12.50m)];

    private const string Payload = "<script>alert('xss')</script>";
    private const string Encoded = "&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;";

    [Fact]
    public void Order_received_encodes_the_special_instructions()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", "ORD-1", "Delivery", 25.00m, "CHF", Items,
            "admin@demo.test", Payload, Payload);

        html.Should().Contain(Encoded).And.NotContain(Payload);
    }

    [Fact]
    public void Order_received_leaves_the_plain_text_body_unencoded()
    {
        var text = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, "Jane Doe", "ORD-1", "Delivery", 25.00m, "CHF", Items,
            "admin@demo.test", Payload);

        text.Should().Contain(Payload).And.NotContain(Encoded);
    }

    [Fact]
    public void Admin_order_alert_encodes_the_customer_identity_and_the_notes()
    {
        var html = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            EmailCultures.English, Brand, "ORD-1", Payload, Payload, Payload, "Delivery", 25.00m, "CHF",
            Items, "https://api.demo.test", "https://demo.test", "admin@demo.test", "token",
            Payload, Payload);

        html.Should().NotContain(Payload);
        html.Should().Contain(Encoded);
    }

    [Fact]
    public void Reservation_mails_encode_the_guest_supplied_fields()
    {
        var date = new DateTime(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc);

        var confirmation = EmailTemplates.ReservationConfirmation.GetHtmlBody(
            EmailCultures.English, Brand, Payload, "T12", date, new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4,
            "admin@demo.test", Payload);
        var approved = EmailTemplates.ReservationApproved.GetHtmlBody(
            EmailCultures.English, Brand, Payload, "T12", date, new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4,
            "admin@demo.test", Payload, Payload);
        var adminNotification = EmailTemplates.ReservationAdminNotification.GetHtmlBody(
            EmailCultures.English, Brand, Guid.Empty, Payload, Payload, Payload, date,
            new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12",
            "https://api.demo.test", "https://demo.test", "admin@demo.test", Payload);

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
