using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The wall clock a human-readable time is printed on (#363). Pure unit tests: the zone is
/// configuration and an instant is an argument, so nothing here needs a host.
/// </summary>
public class TenantClockTests
{
    /// <summary>17 May, so Zurich is on summer time (+02:00); 17 January is +01:00.</summary>
    private static readonly DateTime SummerInstant = new(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime WinterInstant = new(2030, 1, 17, 19, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// The legacy-RUMI case: the main compose project sends no <c>Localization__*</c> key at all.
    /// The answer has to be the zone <c>WorkingHoursService</c> used to hardcode, or this change
    /// silently moves the one live tenant's opening hours.
    /// </summary>
    [Fact]
    public void An_unconfigured_instance_keeps_the_zone_the_code_used_to_hardcode()
    {
        Clock().TimeZone.Id.Should().Be("Europe/Zurich");
        Clock(timeZone: "   ").TimeZone.Id.Should().Be("Europe/Zurich", "a blank key is unset, not a zone");
    }

    [Fact]
    public void A_configured_zone_is_the_one_used()
    {
        var clock = Clock(timeZone: "America/New_York");

        clock.TimeZone.Id.Should().Be("America/New_York");
        clock.ToTenantTime(SummerInstant).Offset.Should().Be(TimeSpan.FromHours(-4));
    }

    /// <summary>
    /// A typo in one tenant's <c>.env</c> must not stop that tenant booting — the same call
    /// <c>EmailLanguageResolver</c> makes about an unusable language list.
    /// </summary>
    [Fact]
    public void An_unknown_zone_falls_back_instead_of_throwing()
    {
        Clock(timeZone: "Europe/Genève").TimeZone.Id.Should().Be("Europe/Zurich");
    }

    /// <summary>
    /// The defect itself: 19:30 UTC is 21:30 in Geneva in May and 20:30 in January. A fixed
    /// two-hour shift would be wrong for half the year, which is why this is a zone and not a
    /// number.
    /// </summary>
    [Fact]
    public void An_instant_lands_on_the_wall_clock_including_the_DST_it_falls_in()
    {
        var clock = Clock();

        clock.ToTenantTime(SummerInstant).Should().Be(
            new DateTimeOffset(2030, 5, 17, 21, 30, 0, TimeSpan.FromHours(2)));
        clock.ToTenantTime(WinterInstant).Should().Be(
            new DateTimeOffset(2030, 1, 17, 20, 30, 0, TimeSpan.FromHours(1)));
    }

    /// <summary>
    /// Every DateTime this database hands back is <see cref="DateTimeKind.Unspecified"/> and means
    /// UTC. Reading it as a local time would be a no-op on the container (whose own zone is UTC)
    /// and wrong on a developer's machine — a defect that could only ever be seen off production.
    /// </summary>
    [Fact]
    public void An_unspecified_kind_is_read_as_UTC()
    {
        var clock = Clock();

        clock.ToTenantTime(DateTime.SpecifyKind(SummerInstant, DateTimeKind.Unspecified))
            .Should().Be(clock.ToTenantTime(SummerInstant));
    }

    /// <summary>
    /// <c>DeletionScheduledAt</c> is <c>UtcNow.AddDays(30)</c>, so a request made in the evening
    /// falls on the NEXT UTC day: the mail named a date the account does not actually survive to.
    /// This is the shift <c>EmailService</c> applies before the date reaches the template.
    /// </summary>
    [Fact]
    public void A_late_evening_local_instant_belongs_to_the_local_day_not_the_UTC_one()
    {
        var lateEvening = new DateTime(2030, 6, 16, 22, 30, 0, DateTimeKind.Utc);

        lateEvening.Date.Day.Should().Be(16, "the stored instant is still on the 16th in UTC");
        Clock().ToTenantTime(lateEvening).Date.Day.Should().Be(17, "but it is already the 17th in Geneva");
    }

    /// <summary>
    /// What a guest actually reads. The marker is the point: "19:30" alone is a promise about a
    /// clock nobody named, and it was the SERVER'S clock.
    /// </summary>
    [Theory]
    [InlineData("en", "Friday, May 17, 2030 21:30 (UTC+02:00)")]
    [InlineData("fr", "vendredi 17 mai 2030 21:30 (UTC+02:00)")]
    [InlineData("de", "Freitag, 17. Mai 2030 21:30 (UTC+02:00)")]
    public void The_rendered_time_carries_the_offset_in_every_language(string language, string expected)
    {
        var moment = Clock().ToTenantTime(SummerInstant);

        EmailTemplates.PasswordChanged.GetTextBody(
                CultureInfo.GetCultureInfo(language),
                new EmailBranding("Demo Restaurant", "Geneva", "contact@demo.test"),
                "Jane",
                "Doe",
                moment)
            .Should().Contain(expected);
    }

    private static TenantClock Clock(string? timeZone = null) =>
        new(
            Options.Create(new LocalizationSettings { TimeZone = timeZone ?? string.Empty }),
            NullLogger<TenantClock>.Instance);
}
