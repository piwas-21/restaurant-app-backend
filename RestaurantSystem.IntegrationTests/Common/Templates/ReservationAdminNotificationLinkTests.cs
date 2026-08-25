using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The alert mail is the ONLY place the quick-approve / quick-reject links are written, so it is
/// the only place a bare, unsigned link can escape into a restaurant's inbox (backend #402).
/// </summary>
/// <remarks>
/// The golden snapshot already pins the whole rendered mail, but it pins it as one 200-line blob;
/// these four facts name the rule, so a future edit that drops the token from one of the two
/// buttons fails with the reason rather than with "the file differs".
/// </remarks>
public class ReservationAdminNotificationLinkTests
{
    private static readonly CultureInfo Culture = EmailCultures.English;
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly EmailGuest Guest = new("Jane Doe", "jane@demo.test", "+41000000");
    private static readonly EmailLinks Links = new("https://api.demo.test", "https://demo.test", "admin@demo.test");
    private static readonly Guid ReservationId = new("11111111-2222-3333-4444-555555555555");

    private static string Render(string? approveToken, string? rejectToken) =>
        EmailTemplates.ReservationAdminNotification.GetHtmlBody(
            Culture, Brand, Guest,
            new ReservationMailDetails(
                new DateTime(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc),
                new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", "Window seat",
                ReservationId, approveToken, rejectToken),
            Links);

    [Fact]
    public void Both_action_buttons_carry_their_own_signature()
    {
        var html = Render("approve-signature", "reject-signature");

        html.Should().Contain($"/api/Reservations/{ReservationId}/quick-approve?token=approve-signature");
        html.Should().Contain($"/api/Reservations/{ReservationId}/quick-reject?token=reject-signature");
    }

    [Fact]
    public void No_button_is_rendered_as_a_bare_token_less_link()
    {
        // Both colour-scheme blocks, so a token added to only one of the two copies fails here.
        var html = Render("approve-signature", "reject-signature");

        html.Should().NotContain("quick-approve'", "a link with no ?token= is the whole of #402");
        html.Should().NotContain("quick-reject'");
        html.Split("quick-approve", StringSplitOptions.None).Should().HaveCount(3, "one link per colour scheme");
        html.Split("quick-reject", StringSplitOptions.None).Should().HaveCount(3);
    }

    [Fact]
    public void A_signature_is_url_escaped_into_the_query_string()
    {
        // Base64url never produces these, but a future token format might, and an unescaped "&"
        // would silently truncate the parameter.
        var html = Render("a+b&c=d", "x y");

        html.Should().Contain("quick-approve?token=a%2Bb%26c%3Dd");
        html.Should().Contain("quick-reject?token=x%20y");
    }

    [Fact]
    public void The_plain_text_body_carries_no_quick_action_link_at_all()
    {
        // It never did. Asserted so that "every template emits a signed URL" cannot be satisfied
        // one day by adding an unsigned link to the half nobody looks at.
        var text = EmailTemplates.ReservationAdminNotification.GetTextBody(
            Culture, Brand, Guest,
            new ReservationMailDetails(
                new DateTime(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc),
                new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12", "Window seat", ReservationId),
            "admin@demo.test");

        text.Should().NotContain("quick-approve");
        text.Should().NotContain("quick-reject");
    }
}
