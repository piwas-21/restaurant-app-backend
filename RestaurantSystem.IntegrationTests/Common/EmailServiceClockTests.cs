using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// What <see cref="EmailService"/> does with a stored instant before it reaches a template (#363).
/// The templates themselves are pinned by the goldens; this is the seam between "UTC in the
/// database" and "the time on the restaurant's wall", and it is the seam the defect lived in.
/// </summary>
public class EmailServiceClockTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// The whole mail, on the tenant's clock and SAYING SO. A guest who did not change their
    /// password has to be able to tell whether 19:30 was them.
    /// </summary>
    [Fact]
    public async Task A_password_changed_mail_prints_the_tenant_wall_clock_with_its_offset()
    {
        var (service, sent, clock) = Service();

        await service.SendPasswordChangedNotificationAsync(English, User());

        var expected = $"{clock.Now:HH:mm} (UTC{clock.Now:zzz})";

        sent.Single().HtmlBody.Should().Contain(expected);
        sent.Single().TextBody.Should().Contain(expected);
    }

    /// <summary>
    /// Both bodies carry the SAME minute. Honest about its own reach: the two-<c>DateTime.UtcNow</c>
    /// version this replaced only disagreed when the calls straddled a minute boundary, which no
    /// test can provoke deterministically — so this pins the shape (one marker, one minute, both
    /// halves) rather than claiming to catch the race.
    /// </summary>
    [Fact]
    public async Task Both_bodies_of_one_mail_agree_about_the_minute()
    {
        var (service, sent, _) = Service();

        await service.SendPasswordChangedNotificationAsync(English, User());

        var mail = sent.Single();
        var html = MinuteIn(mail.HtmlBody);

        html.Should().NotBeEmpty();
        MinuteIn(mail.TextBody!).Should().Be(html);
    }

    /// <summary>
    /// A deletion scheduled at 22:30 UTC is already tomorrow in Geneva. The mail names a DAY, so
    /// it must name the day the guest is living in — the version that reads the raw UTC value
    /// tells them their account dies a day later than it does.
    /// </summary>
    [Fact]
    public async Task An_account_deletion_mail_names_the_local_day_not_the_UTC_one()
    {
        var (service, sent, _) = Service();

        await service.SendAccountDeletionEmailAsync(
            English,
            "jane@demo.test",
            "Jane",
            "Doe",
            "https://demo.test/delete",
            "https://demo.test/cancel",
            new DateTime(2030, 6, 16, 22, 30, 0, DateTimeKind.Utc));

        sent.Single().HtmlBody.Should().Contain("Monday, June 17, 2030")
            .And.NotContain("Sunday, June 16, 2030");
    }

    private static string MinuteIn(string body)
    {
        var match = System.Text.RegularExpressions.Regex.Match(body, @"\d{2}:\d{2} \(UTC[+-]\d{2}:\d{2}\)");

        return match.Success ? match.Value : string.Empty;
    }

    private static ApplicationUser User() => new()
    {
        UserName = "jane@demo.test",
        Email = "jane@demo.test",
        FirstName = "Jane",
        LastName = "Doe",
        Role = UserRole.Customer,
        CreatedBy = "test",
        RefreshToken = string.Empty
    };

    private static (EmailService Service, List<OutgoingEmail> Sent, ITenantClock Clock) Service()
    {
        var sent = new List<OutgoingEmail>();
        var clock = new TenantClock(
            Options.Create(new LocalizationSettings()), NullLogger<TenantClock>.Instance);

        var service = new EmailService(
            Options.Create(new EmailSettings
            {
                Provider = "Smtp",
                EmailsEnabled = true,
                LogEmailsOnly = false,
                FromEmail = "info@demo.test",
                FromName = "Demo Restaurant",
                AdminEmail = "owner@demo.test",
                FrontendBaseUrl = "https://demo.test",
                BackendBaseUrl = "https://api.demo.test"
            }),
            Options.Create(new LocalizationSettings()),
            new CapturingSender(sent),
            new FixedBranding(),
            clock,
            NullLogger<EmailService>.Instance);

        return (service, sent, clock);
    }

    private sealed class CapturingSender(List<OutgoingEmail> sent) : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            sent.Add(email);

            return Task.CompletedTask;
        }
    }

    private sealed class FixedBranding : IEmailBrandingProvider
    {
        public Task<EmailBranding> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new EmailBranding("Demo Restaurant", "Geneva", "contact@demo.test"));
    }
}
