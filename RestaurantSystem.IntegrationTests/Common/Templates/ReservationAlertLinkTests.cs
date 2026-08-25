using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The operator alerts are the ONLY place the quick-approve / quick-reject links are written, so
/// they are the only place a bare, unsigned link can escape into a restaurant's inbox
/// (backend #402).
/// </summary>
/// <remarks>
/// The golden snapshots already pin the whole rendered mails, but they pin them as 200-line blobs;
/// these four facts name the rule, so a future edit that drops the token from one of the buttons
/// fails with the reason rather than with "the file differs".
/// <para>
/// Run over BOTH alerts since backend #407 added the second one (the changed-booking alert). A
/// suite that knew about one template would have gone on passing while the new mail shipped
/// token-less links — which is exactly the regression #402 exists to prevent. Both render through
/// the same shared helper, and that is the property being defended, not an implementation detail.
/// </para>
/// </remarks>
public class ReservationAlertLinkTests
{
    private const string NewBooking = "ReservationAdminNotification";
    private const string ChangedBooking = "ReservationChangedAdmin";

    private static readonly CultureInfo Culture = EmailCultures.English;
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly EmailGuest Guest = new("Jane Doe", "jane@demo.test", "+41000000");
    private static readonly EmailLinks Links = new("https://api.demo.test", "https://demo.test", "admin@demo.test");
    private static readonly Guid ReservationId = new("11111111-2222-3333-4444-555555555555");
    private static readonly ReservationPreviousBooking Previous = new(
        new DateTime(2030, 5, 16, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(18, 0, 0), new TimeSpan(20, 0, 0),
        2, WasConfirmed: true);

    public static TheoryData<string> Alerts() => new() { NewBooking, ChangedBooking };

    private static ReservationMailDetails Booking(string? approveToken, string? rejectToken) =>
        new(new DateTime(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc),
            new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", "Window seat",
            ReservationId, approveToken, rejectToken);

    private static string Render(string alert, string? approveToken, string? rejectToken) =>
        alert == NewBooking
            ? EmailTemplates.ReservationAdminNotification.GetHtmlBody(
                Culture, Brand, Guest, Booking(approveToken, rejectToken), Links)
            : EmailTemplates.ReservationChangedAdmin.GetHtmlBody(
                Culture, Brand, Guest, Booking(approveToken, rejectToken), Previous, Links);

    [Theory]
    [MemberData(nameof(Alerts))]
    public void Both_action_buttons_carry_their_own_signature(string alert)
    {
        var html = Render(alert, "approve-signature", "reject-signature");

        html.Should().Contain($"/api/Reservations/{ReservationId}/quick-approve?token=approve-signature");
        html.Should().Contain($"/api/Reservations/{ReservationId}/quick-reject?token=reject-signature");
    }

    [Theory]
    [MemberData(nameof(Alerts))]
    public void No_button_is_rendered_as_a_bare_token_less_link(string alert)
    {
        // Both colour-scheme blocks, so a token added to only one of the two copies fails here.
        var html = Render(alert, "approve-signature", "reject-signature");

        html.Should().NotContain("quick-approve'", "a link with no ?token= is the whole of #402");
        html.Should().NotContain("quick-reject'");
        html.Split("quick-approve", StringSplitOptions.None).Should().HaveCount(3, "one link per colour scheme");
        html.Split("quick-reject", StringSplitOptions.None).Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(Alerts))]
    public void A_signature_is_url_escaped_into_the_query_string(string alert)
    {
        // Base64url never produces these, but a future token format might, and an unescaped "&"
        // would silently truncate the parameter.
        var html = Render(alert, "a+b&c=d", "x y");

        html.Should().Contain("quick-approve?token=a%2Bb%26c%3Dd");
        html.Should().Contain("quick-reject?token=x%20y");
    }

    [Theory]
    [MemberData(nameof(Alerts))]
    public void The_plain_text_body_carries_no_quick_action_link_at_all(string alert)
    {
        // It never did. Asserted so that "every template emits a signed URL" cannot be satisfied
        // one day by adding an unsigned link to the half nobody looks at.
        var booking = Booking("approve-signature", "reject-signature");

        var text = alert == NewBooking
            ? EmailTemplates.ReservationAdminNotification.GetTextBody(
                Culture, Brand, Guest, booking, "admin@demo.test")
            : EmailTemplates.ReservationChangedAdmin.GetTextBody(
                Culture, Brand, Guest, booking, Previous, "admin@demo.test");

        text.Should().NotContain("quick-approve");
        text.Should().NotContain("quick-reject");
    }
}
