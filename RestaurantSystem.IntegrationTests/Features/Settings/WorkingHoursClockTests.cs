using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Settings;

/// <summary>
/// "Are we open now?" on the TENANT's clock (#363). This is the higher-consequence half of that
/// change: the answer gates ordering through <c>OrderTypeConfigurationService</c>, so an hour of
/// drift closes a restaurant that is serving, or takes orders it cannot cook.
/// <para>
/// The instant is chosen so that UTC and the wall clock disagree about BOTH the hour and the day —
/// the previous code read <c>DateTime.UtcNow</c> and would answer "closed" and "Friday" here.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class WorkingHoursClockTests : IntegrationTestBase
{
    /// <summary>Friday 22:30 UTC — which is already Saturday 00:30 in Zurich.</summary>
    private static readonly DateTimeOffset Instant =
        new(2030, 5, 17, 22, 30, 0, TimeSpan.Zero);

    public WorkingHoursClockTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(new FixedClock(Instant, "Europe/Zurich"));
    }

    [Fact]
    public async Task Open_is_decided_on_the_tenant_wall_clock_not_UTC()
    {
        await SeedHoursAsync(DayOfWeek.Saturday, open: new TimeSpan(0, 0, 0), close: new TimeSpan(2, 0, 0));

        using var scope = Factory.Services.CreateScope();
        var hours = scope.ServiceProvider.GetRequiredService<IWorkingHoursService>();

        // 00:30 local is inside Saturday's 00:00-02:00 service. 22:30 UTC on FRIDAY is not.
        (await hours.IsOpenNowAsync()).Should().BeTrue();
        (await hours.GetTodayHoursAsync())!.DayOfWeek.Should().Be(DayOfWeek.Saturday);
    }

    [Fact]
    public async Task A_venue_closed_on_the_local_day_is_closed()
    {
        await SeedHoursAsync(DayOfWeek.Saturday, open: new TimeSpan(11, 0, 0), close: new TimeSpan(14, 0, 0));

        using var scope = Factory.Services.CreateScope();
        var hours = scope.ServiceProvider.GetRequiredService<IWorkingHoursService>();

        (await hours.IsOpenNowAsync()).Should().BeFalse("00:30 is outside Saturday lunch service");
    }

    private async Task SeedHoursAsync(DayOfWeek day, TimeSpan open, TimeSpan close)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await context.WorkingHours.ToListAsync();
        context.WorkingHours.RemoveRange(existing);

        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = day,
            OpenTime = open,
            CloseTime = close,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A clock stopped at a known instant. Hand-written rather than mocked: the whole value of
    /// <see cref="ITenantClock"/> being an interface is that a test can hold one.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset instant, string zoneId) : ITenantClock
    {
        public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        public DateTimeOffset Now => ToTenantTime(instant.UtcDateTime);

        public DateTimeOffset ToTenantTime(DateTime value) =>
            TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)), TimeZone);
    }
}
