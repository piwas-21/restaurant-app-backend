using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task The_smallest_fitting_single_table_is_chosen_without_table_details_in_the_response(int party)
    {
        await ResolveAsync(Request() with { NumberOfGuests = party });
        await using var db = DatabaseFixture.CreateContext();
        var row = await db.Reservations.Include(value => value.CombinedTables).SingleAsync();
        Assert.Equal(party <= 2 ? _second : _first, row.TableId); Assert.Empty(row.CombinedTables);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("gap")]
    [InlineData("duration")]
    [InlineData("past")]
    [InlineData("capacity")]
    [InlineData("timezone")]
    [InlineData("tenant")]
    public async Task Current_engine_capacity_calendar_and_original_context_are_authoritative(string mode)
    {
        var request = Request();
        var expected = HttpStatusCode.Conflict;
        await using var db = DatabaseFixture.CreateContext();
        if (mode == "closed") await db.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, true));
        else if (mode == "gap") request = request with { StartTime = TimeSpan.FromHours(16), EndTime = TimeSpan.FromHours(18) };
        else if (mode == "duration") request = request with { EndTime = TimeSpan.FromHours(19) };
        else if (mode == "past") request = request with { Date = DateOnly.FromDateTime(Day.AddDays(-10)) };
        else if (mode == "capacity") request = request with { NumberOfGuests = 7 };
        else if (mode == "timezone")
        {
            await PrepareAsync(request);
            _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.FindSystemTimeZoneById("UTC"));
            expected = HttpStatusCode.NotFound;
        }
        else { request = request with { TenantId = Guid.NewGuid() }; expected = HttpStatusCode.NotFound; }
        Assert.Equal(expected, (await SendAsync(request)).StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("deleted")]
    [InlineData("capability")]
    public async Task Native_creation_waits_for_shared_schedule_authority_then_checks_current_customer_or_rollout(string mode)
    {
        Authenticate();
        await using var holder = DatabaseFixture.CreateContext();
        await using var held = await ReservationWriteLock.BeginAsync(holder, default);
        var pending = SendAsync(Request()); var released = false;
        try
        {
            await WaitForScheduleAsync(); Assert.False(pending.IsCompleted);
            if (mode == "role") await holder.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
            else if (mode == "deleted") await holder.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            else Mobile.EnabledCapabilities = [];
            await held.CommitAsync(); released = true;
            Assert.Equal(HttpStatusCode.NotFound, (await pending).StatusCode);
        }
        finally
        {
            if (!released) await held.RollbackAsync();
            await pending;
        }
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Empty(await verify.Reservations.ToListAsync()); Assert.Empty(await verify.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData("customerPhone")]
    [InlineData("specialRequests")]
    public async Task Tenant_required_optional_fields_are_checked_by_the_shared_writer(string field)
    {
        await using var db = DatabaseFixture.CreateContext();
        var configured = await db.Set<CustomerFormFieldConfiguration>()
            .SingleOrDefaultAsync(row => row.FormKey == "reservation" && row.FieldKey == field);
        if (configured is null)
        {
            configured = new CustomerFormFieldConfiguration { FormKey = "reservation", FieldKey = field, CreatedBy = "test" };
            db.Add(configured);
        }
        configured.IsRequired = true; configured.IsVisible = true;
        await db.SaveChangesAsync();
        var request = Request() with { SpecialRequests = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(request)).StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        var corrected = field == "customerPhone" ? request with { CustomerPhone = "12345" }
            : request with { SpecialRequests = "Customer request" };
        Assert.Equal("Applied", (await ResolveAsync(corrected)).GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_attempt_rechecks_the_selected_tenant_after_the_real_schedule_wait(bool alreadyApplied)
    {
        var request = Request();
        if (alreadyApplied) await ResolveAsync(request);
        await using var holder = DatabaseFixture.CreateContext();
        await using var held = await ReservationWriteLock.BeginAsync(holder, default);
        var pending = SendAsync(request, true); var released = false;
        try
        {
            await WaitForScheduleAsync(); Assert.False(pending.IsCompleted);
            Mobile.TenantId = Guid.NewGuid();
            await held.CommitAsync(); released = true;
            Assert.Equal(HttpStatusCode.NotFound, (await pending).StatusCode);
        }
        finally
        {
            if (!released) await held.RollbackAsync();
            await pending;
        }
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(alreadyApplied ? 1 : 0, await verify.Set<CustomerReservationOperation>().CountAsync());
        Assert.Equal(alreadyApplied ? 1 : 0, _mailCalls);
    }

    [Fact]
    public async Task A_separate_table_admin_capacity_reduction_after_slots_is_read_fresh_before_booking()
    {
        var reduced = false;
        _afterSlots = () =>
        {
            using var admin = DatabaseFixture.CreateContext();
            admin.Tables.Where(row => row.Id == _first).ExecuteUpdate(update => update.SetProperty(row => row.MaxGuests, 1));
            reduced = true;
        };
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(Request())).StatusCode);
        Assert.True(reduced);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(3, await verify.Tables.SumAsync(row => row.MaxGuests));
        Assert.Empty(await verify.Reservations.ToListAsync()); Assert.Empty(await verify.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_start_which_elapses_after_slot_calculation_cannot_be_accepted(int elapsedMinutes)
    {
        _afterSlots = () => _clock.SetupGet(value => value.Now).Returns(
            new DateTimeOffset(2030, 5, 17, 18, elapsedMinutes, 0, TimeSpan.FromHours(2)));
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(Request())).StatusCode);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Empty(await verify.Reservations.ToListAsync()); Assert.Empty(await verify.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    private async Task WaitForScheduleAsync()
    {
        await using var observer = DatabaseFixture.CreateContext();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var waiters = await observer.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                    AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                """).SingleAsync();
            if (waiters > 0) return;
            await Task.Delay(25);
        }
        Assert.Fail("The native HTTP writer never waited on this database's real schedule lock.");
    }
}
