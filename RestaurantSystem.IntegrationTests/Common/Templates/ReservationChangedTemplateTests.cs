using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The three outcomes of a guest's own edit, rendered in every language the reservation family is
/// translated into (backend #407).
/// </summary>
/// <remarks>
/// The golden snapshot pins ONE outcome — the reshaped confirmed booking — in English. That leaves
/// the other two status paragraphs rendered by nothing, and a missing resource key does not fail
/// quietly: <c>EmailText</c> throws, inside a send that the mailer swallows, so the guest would
/// simply never get the mail. This suite renders every branch in every translated culture for that
/// reason, and asserts the three say different things, which a copy-paste of one key into three
/// would not.
/// </remarks>
public class ReservationChangedTemplateTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly ReservationMailDetails Booking = new(
        new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0),
        4, "T12", "Window seat", Guid.Parse("11111111-2222-3333-4444-555555555555"));
    private static readonly ReservationPreviousBooking Previous = new(
        new DateTime(2030, 5, 16, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(18, 0, 0), new TimeSpan(20, 0, 0),
        2, WasConfirmed: true);

    public static TheoryData<string> Cultures()
    {
        var data = new TheoryData<string> { EmailCultures.English.Name };
        foreach (var culture in EmailResources.Translated)
        {
            data.Add(culture.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Each_outcome_says_something_different_to_the_guest(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        var bodies = new[]
        {
            ReservationChangeOutcome.StillConfirmed,
            ReservationChangeOutcome.AwaitingApproval,
            ReservationChangeOutcome.NeedsApprovalAgain
        }.Select(outcome => EmailTemplates.ReservationChanged.GetTextBody(
            culture, Brand, "Jane Doe", Booking, outcome, "admin@demo.test")).ToList();

        bodies.Should().OnlyHaveUniqueItems(
            "a guest whose confirmation was withdrawn must not read the same paragraph as one whose "
            + "booking never lost it");
        bodies.Should().AllSatisfy(body => body.Should().NotContain("{0}"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_outcome_renders_in_html_too(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        foreach (var outcome in Enum.GetValues<ReservationChangeOutcome>())
        {
            var html = EmailTemplates.ReservationChanged.GetHtmlBody(
                culture, Brand, "Jane Doe", Booking, outcome, "admin@demo.test");

            html.Should().Contain(Brand.Name).And.NotContain("{0}");
        }
    }

    /// <summary>
    /// The operator alert's two branches — an approval withdrawn, and a booking that was pending
    /// all along — plus the links that are the only reason it is a mail and not a dashboard badge.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_operator_alert_names_the_state_it_found_and_carries_the_quick_actions(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var links = new EmailLinks("https://api.demo.test", "https://demo.test", "admin@demo.test");

        var withdrawn = EmailTemplates.ReservationChangedAdmin.GetHtmlBody(
            culture, Brand, new EmailGuest("Jane Doe", "jane@demo.test", "+41000000"), Booking, Previous, links);
        var stillPending = EmailTemplates.ReservationChangedAdmin.GetHtmlBody(
            culture, Brand, new EmailGuest("Jane Doe", "jane@demo.test", "+41000000"), Booking,
            Previous with { WasConfirmed = false }, links);

        withdrawn.Should().NotBe(stillPending);
        withdrawn.Should().Contain($"https://api.demo.test/api/Reservations/{Booking.Id}/quick-approve");
        withdrawn.Should().Contain($"https://api.demo.test/api/Reservations/{Booking.Id}/quick-reject");
        stillPending.Should().NotContain("{0}");
    }

    /// <summary>
    /// The text body has its own copy of both branches, and its own set of keys — the HTML passing
    /// says nothing about it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_operator_alert_renders_both_branches_as_text(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var guest = new EmailGuest("Jane Doe", "jane@demo.test", "+41000000");

        var withdrawn = EmailTemplates.ReservationChangedAdmin.GetTextBody(
            culture, Brand, guest, Booking, Previous, "admin@demo.test");
        var stillPending = EmailTemplates.ReservationChangedAdmin.GetTextBody(
            culture, Brand, guest, Booking, Previous with { WasConfirmed = false }, "admin@demo.test");

        withdrawn.Should().NotBe(stillPending);
        withdrawn.Should().NotContain("{0}");
        stillPending.Should().NotContain("{0}");
    }
}
