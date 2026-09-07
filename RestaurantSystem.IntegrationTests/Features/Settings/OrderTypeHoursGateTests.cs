using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Settings;

/// <summary>
/// The opening-hours gate is PER ORDER TYPE (#448). Before this change the service removed ONLY
/// DineIn while the restaurant was closed, so a shut restaurant happily accepted a delivery order
/// at 04:00. Gating is now a per-type column on <c>OrderTypeConfiguration</c>, and the column's
/// defaults reproduce the OLD behaviour per type: DineIn enforced (the one type that has always
/// been gated), Takeaway/Delivery not.
/// <para>
/// The defaults are the load-bearing half: RUMI takes late takeaway orders today, and a migration
/// or a self-heal that flipped a default to "enforced" would silently stop those orders. Refusal
/// keeps the shape it always had for DineIn — the type is REMOVED from the offered set, it does
/// not become an error.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class OrderTypeHoursGateTests : IntegrationTestBase
{
    /// <summary>2030-05-17 is a Friday; Zurich is on CEST that day, so local = UTC + 2.</summary>
    private const int ZurichSummerOffsetHours = 2;

    private static readonly TimeSpan ServiceOpen = new(11, 0, 0);
    private static readonly TimeSpan ServiceClose = new(23, 0, 0);

    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 17, 2, 0, 0, TimeSpan.Zero), "Europe/Zurich");

    public OrderTypeHoursGateTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(_clock);
    }

    // ── The defaults are today's behaviour ───────────────────────────────

    [Fact]
    public async Task Outside_hours_the_defaults_still_gate_ONLY_DineIn()
    {
        await SeedAsync();
        AtLocalTime(4, 0); // long before the 11:00 service

        var enabled = await GetEnabledAsync();

        enabled.Should().NotContain(OrderType.DineIn,
            "DineIn has always been refused while the restaurant is closed");
        enabled.Should().Contain(OrderType.Takeaway,
            "the default for takeaway is NOT enforced — a shut shop still takes the overnight order");
        enabled.Should().Contain(OrderType.Delivery,
            "the default for delivery is NOT enforced");
    }

    [Fact]
    public async Task The_read_model_reports_the_default_gate_per_type()
    {
        await SeedAsync();

        var configurations = await GetAllAsync();

        configurations.Single(c => c.OrderType == OrderType.DineIn).EnforceOpeningHours.Should().BeTrue();
        configurations.Single(c => c.OrderType == OrderType.Takeaway).EnforceOpeningHours.Should().BeFalse();
        configurations.Single(c => c.OrderType == OrderType.Delivery).EnforceOpeningHours.Should().BeFalse();
    }

    // ── The gate, per type ───────────────────────────────────────────────

    [Fact]
    public async Task Outside_hours_an_enforced_TAKEAWAY_is_refused_while_delivery_still_works()
    {
        await SeedAsync();
        AtLocalTime(4, 0);

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);

        var enabled = await GetEnabledAsync();
        enabled.Should().NotContain(OrderType.Takeaway);
        enabled.Should().Contain(OrderType.Delivery,
            "the gate answers per type, never all types at once");
        enabled.Should().NotContain(OrderType.DineIn, "the untouched DineIn default still gates");
    }

    [Fact]
    public async Task Outside_hours_an_enforced_DELIVERY_is_refused_while_takeaway_still_works()
    {
        await SeedAsync();
        AtLocalTime(4, 0);

        await PutAsync(OrderType.Delivery, isEnabled: true, enforceOpeningHours: true);

        var enabled = await GetEnabledAsync();
        enabled.Should().NotContain(OrderType.Delivery);
        enabled.Should().Contain(OrderType.Takeaway);
    }

    [Fact]
    public async Task Flipping_the_gate_back_off_restores_overnight_orders()
    {
        await SeedAsync();
        AtLocalTime(4, 0);

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);
        (await GetEnabledAsync()).Should().NotContain(OrderType.Takeaway);

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: false);

        (await GetEnabledAsync()).Should().Contain(OrderType.Takeaway,
            "an admin can always go back to accepting orders at any hour");
    }

    // ── Inside hours the enforced column changes nothing ─────────────────

    [Fact]
    public async Task Inside_hours_every_enabled_type_is_offered_even_when_enforced()
    {
        await SeedAsync();
        AtLocalTime(12, 0); // mid service

        await PutAsync(OrderType.Delivery, isEnabled: true, enforceOpeningHours: true);
        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);

        (await GetEnabledAsync()).Should().BeEquivalentTo(
            [OrderType.DineIn, OrderType.Takeaway, OrderType.Delivery]);
    }

    // ── Boundary instants, on the same inclusive rule as IsOpenNowAsync ──

    [Theory]
    [InlineData(10, 59, false)] // one minute before service
    [InlineData(11, 0, true)]   // the exact open minute is inside
    [InlineData(23, 0, true)]   // the exact close minute is inside — inclusive, as one window always was
    [InlineData(23, 1, false)]  // one minute after service
    public async Task The_boundary_instants_follow_the_open_interval(int hour, int minute, bool offered)
    {
        await SeedAsync();
        AtLocalTime(hour, minute);

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);

        var enabled = await GetEnabledAsync();

        if (offered)
        {
            enabled.Should().Contain(OrderType.Takeaway,
                $"{hour:00}:{minute:00} is inside the 11:00-23:00 service (bounds inclusive)");
        }
        else
        {
            enabled.Should().NotContain(OrderType.Takeaway,
                $"{hour:00}:{minute:00} is outside the 11:00-23:00 service");
        }

        enabled.Should().Contain(OrderType.Delivery, "an unenforced type is offered at ANY hour");
    }

    // ── After-midnight service ───────────────────────────────────────────

    [Fact]
    public async Task After_midnight_service_reads_the_early_window_of_its_own_day()
    {
        // A window that crosses midnight cannot be stored as one pair (the write path refuses
        // close < open); the model expresses late-night service as an EARLY window owned by the
        // day it starts in — Saturday 00:00-02:00 covers Saturday's after-midnight trade. The
        // gate inherits that model unchanged: it is the same read IsOpenNowAsync makes.
        await SeedAsync(saturdayOpen: new TimeSpan(0, 0, 0), saturdayClose: new TimeSpan(2, 0, 0));

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);

        AtLocalTime(1, 30, day: DayOfWeek.Saturday);
        (await GetEnabledAsync()).Should().Contain(OrderType.Takeaway,
            "01:30 Saturday is inside Saturday's 00:00-02:00 window");

        AtLocalTime(2, 30, day: DayOfWeek.Saturday);
        (await GetEnabledAsync()).Should().NotContain(OrderType.Takeaway);
    }

    // ── The write path must not interpret an omitted field ───────────────

    [Fact]
    public async Task A_update_that_omits_the_gate_field_leaves_it_unchanged()
    {
        // The shipped frontend sends exactly { orderType, isEnabled }. A body without
        // enforceOpeningHours must not read as false and silently switch the gate off.
        await SeedAsync();
        AtLocalTime(12, 0);

        await PutAsync(OrderType.Takeaway, isEnabled: true, enforceOpeningHours: true);

        // Deliberately no enforceOpeningHours in the payload.
        var response = await Client.PutAsJsonAsync("/api/OrderTypeConfiguration",
            new { orderType = nameof(OrderType.Takeaway), isEnabled = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var configurations = await GetAllAsync();
        configurations.Single(c => c.OrderType == OrderType.Takeaway)
            .EnforceOpeningHours.Should().BeTrue("omitted means unchanged, not off");
    }

    // ── Self-healed rows come back with their historical default ─────────

    [Fact]
    public async Task A_deleted_row_is_self_healed_with_the_type_historical_default()
    {
        await SeedAsync();
        AtLocalTime(12, 0);

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var doomed = await context.OrderTypeConfigurations
                .Where(c => c.OrderType == OrderType.Takeaway || c.OrderType == OrderType.DineIn)
                .ToListAsync();
            context.OrderTypeConfigurations.RemoveRange(doomed);
            await context.SaveChangesAsync();
        }

        var configurations = await GetAllAsync(); // GetAllAsync runs the self-heal

        configurations.Single(c => c.OrderType == OrderType.DineIn).EnforceOpeningHours.Should().BeTrue(
            "DineIn was the gated type before the column existed");
        configurations.Single(c => c.OrderType == OrderType.Takeaway).EnforceOpeningHours.Should().BeFalse(
            "takeaway was accepted at any hour before the column existed");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the clock to <paramref name="hour"/>:<paramref name="minute"/> as the RESTAURANT reads
    /// it, and asserts that it landed there — the offset arithmetic is the test's own, so an error
    /// in it would otherwise silently retarget every assertion above.
    /// </summary>
    private void AtLocalTime(int hour, int minute, DayOfWeek day = DayOfWeek.Friday)
    {
        var baseUtc = new DateTimeOffset(2030, 5, 17, 0, 0, 0, TimeSpan.Zero); // a Friday
        var dayShift = ((int)day - (int)DayOfWeek.Friday + 7) % 7;
        _clock.Set(
            baseUtc.AddDays(dayShift).AddHours(hour - ZurichSummerOffsetHours).AddMinutes(minute),
            "Europe/Zurich");

        _clock.Now.TimeOfDay.Should().Be(new TimeSpan(hour, minute, 0));
        _clock.Now.DayOfWeek.Should().Be(day);
    }

    private async Task<List<OrderType>> GetEnabledAsync()
    {
        var response = await Client.GetAsync("/api/OrderTypeConfiguration/enabled");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<OrderType>>>(JsonOptions);
        body!.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<List<OrderTypeConfigurationDto>> GetAllAsync()
    {
        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/OrderTypeConfiguration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<OrderTypeConfigurationDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<HttpResponseMessage> PutAsync(OrderType orderType, bool isEnabled, bool? enforceOpeningHours)
    {
        AuthenticateAsAdmin();
        var response = await Client.PutAsJsonAsync("/api/OrderTypeConfiguration", new
        {
            orderType = orderType.ToString(),
            isEnabled,
            enforceOpeningHours,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response;
    }

    /// <summary>
    /// The database state a migrated tenant is in: Friday (and optionally Saturday) hours, plus
    /// one configuration row per order type carrying the backfilled gate defaults.
    /// </summary>
    private async Task SeedAsync(TimeSpan? saturdayOpen = null, TimeSpan? saturdayClose = null)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WorkingHours.RemoveRange(await context.WorkingHours.ToListAsync());
        context.OrderTypeConfigurations.RemoveRange(await context.OrderTypeConfigurations.ToListAsync());

        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            OpenTime = ServiceOpen,
            CloseTime = ServiceClose,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        });

        if (saturdayOpen.HasValue && saturdayClose.HasValue)
        {
            context.WorkingHours.Add(new WorkingHours
            {
                DayOfWeek = DayOfWeek.Saturday,
                OpenTime = saturdayOpen.Value,
                CloseTime = saturdayClose.Value,
                IsActive = true,
                IsClosed = false,
                CreatedBy = "test"
            });
        }

        foreach (var type in Enum.GetValues<OrderType>())
        {
            context.OrderTypeConfigurations.Add(new OrderTypeConfiguration
            {
                OrderType = type,
                IsEnabled = true,
                DisplayOrder = (int)type,
                EnforceOpeningHours = OrderTypeConfiguration.EnforcedByDefault(type),
                CreatedBy = "test"
            });
        }

        await context.SaveChangesAsync();
    }
}
