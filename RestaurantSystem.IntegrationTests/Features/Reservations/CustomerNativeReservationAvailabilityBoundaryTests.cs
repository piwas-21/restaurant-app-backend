using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationAvailabilityTests
{
    [Theory]
    [InlineData("capability")]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("module")]
    [InlineData("currency")]
    public async Task Current_native_access_is_required_even_for_public_availability(string mode)
    {
        await SeedAsync();
        Assert.NotEmpty(Starts(await ReadAsync()));
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        if (mode == "capability") mobile.EnabledCapabilities = [];
        else if (mode == "module") _modules.Setup(value => value.IsEnabled("reservations")).Returns(false);
        else if (mode == "currency")
        {
            await using var context = DatabaseFixture.CreateContext();
            await context.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
        }
        else mobile.Lifecycle = mode;
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + Query)).StatusCode);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("tenant")]
    [InlineData("timezone")]
    public async Task Withdrawal_after_real_slot_materialization_prevents_a_stale_public_result(string mode)
    {
        await SeedAsync();
        Assert.NotEmpty(Starts(await ReadAsync()));
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        var observed = false;
        _afterSlots = () =>
        {
            observed = true;
            if (mode == "capability") mobile.EnabledCapabilities = [];
            else if (mode == "tenant") mobile.TenantId = Guid.NewGuid();
            else _clock.Set(new DateTimeOffset(2030, 5, 10, 9, 0, 0, TimeSpan.Zero), "Europe/Zurich");
        };
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + Query)).StatusCode);
        Assert.True(observed);
    }

    [Fact]
    public async Task Today_is_the_restaurant_day_and_elapsed_wall_slots_are_never_offered()
    {
        await SeedAsync();
        // UTC is already Saturday. In Los Angeles it is still Friday at19:30.
        _clock.Set(new DateTimeOffset(2030, 5, 18, 2, 30, 0, TimeSpan.Zero), "America/Los_Angeles");
        Assert.Equal(new[] { "20:00:00", "20:30:00", "21:00:00" }, Starts(await ReadAsync()));
        _clock.Set(new DateTimeOffset(2030, 5, 18, 7, 30, 0, TimeSpan.Zero), "America/Los_Angeles");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + Query)).StatusCode);
    }

    [Fact]
    public async Task Repeated_DST_hour_preserves_the_same_calendar_day_and_restaurant_wall_slots()
    {
        await SeedAsync();
        await using var context = DatabaseFixture.CreateContext();
        await context.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.DayOfWeek, DayOfWeek.Sunday));
        var expected = new[] { "11:00:00", "11:30:00", "12:00:00", "12:30:00", "13:00:00",
            "18:00:00", "18:30:00", "19:00:00", "19:30:00", "20:00:00", "20:30:00", "21:00:00" };
        foreach (var utcHour in new[] { 8, 9 })
        {
            _clock.Set(new DateTimeOffset(2026, 11, 1, utcHour, 30, 0, TimeSpan.Zero), "America/Los_Angeles");
            var page = await ReadAsync("?schemaVersion=1&date=2026-11-01&numberOfGuests=6");
            Assert.Equal("2026-11-01", page.GetProperty("date").GetString());
            Assert.Equal(expected, Starts(page));
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(20, true)]
    public async Task Both_documented_party_size_limits_are_valid_reads(int party, bool empty)
    {
        await SeedAsync();
        var page = await ReadAsync("?schemaVersion=1&date=2030-05-17&numberOfGuests=" + party);
        Assert.Equal(party, page.GetProperty("numberOfGuests").GetInt32());
        Assert.Equal(empty, Starts(page).Length == 0);
    }

    [Fact]
    public async Task Closed_days_return_an_explicit_empty_page_and_missing_tables_never_claim_successful_availability()
    {
        await SeedAsync();
        Assert.NotEmpty(Starts(await ReadAsync()));
        await using var context = DatabaseFixture.CreateContext();
        await context.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, true));
        Assert.Empty(Starts(await ReadAsync()));
        await context.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, false));
        await context.Tables.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + Query)).StatusCode);
    }
}
