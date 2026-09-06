using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// The combine-tables flow, backend #561. Before it, the /reservations page told a party bigger
/// than any single table to select several tables and "request to combine them" — and then posted
/// ONE reservation per table, each carrying the FULL party size, and the create handler refused
/// every one of them on per-table capacity. The contract now: ONE reservation over N tables, the
/// capacity rule reads the SUM of the set (individual tables may be smaller than the party — that
/// is the point), and every occupancy read of a slot sees the combined tables as occupied.
/// </summary>
[Collection("Database Lane 2")]
public class CombinedTableReservationTests : IntegrationTestBase
{
    /// <summary>A Friday far enough ahead that no slot is filtered as past.</summary>
    private const string BookedDay = "2030-05-17";

    private Guid _t1, _t2, _t3, _t4;

    public CombinedTableReservationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // One 18:00-23:00 window: 2h slots on 30-minute starts, so the first dinner slot is
        // 18:00 and the last is 21:00.
        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test",
            Shifts =
            {
                new WorkingHoursShift { OpenTime = new TimeSpan(18, 0, 0), CloseTime = new TimeSpan(23, 0, 0), CreatedBy = "test" }
            }
        });

        var t1 = new Table { TableNumber = "C1", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
        var t2 = new Table { TableNumber = "C2", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
        var t3 = new Table { TableNumber = "C3", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
        var t4 = new Table { TableNumber = "C4", MaxGuests = 2, IsActive = true, CreatedBy = "test" };
        context.Tables.AddRange(t1, t2, t3, t4);
        await context.SaveChangesAsync();

        _t1 = t1.Id; _t2 = t2.Id; _t3 = t3.Id; _t4 = t4.Id;
    }

    private static StringContent Body(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<ApiResponse<ReservationDto>> CreateAsync(object payload)
    {
        var response = await Client.PostAsync("/api/reservations", Body(payload));
        var body = await ReadResponseAsync<ApiResponse<ReservationDto>>(response);
        body.Should().NotBeNull();
        return body!;
    }

    /// <summary>The refusal's sentence: <c>ApiResponse.Failure</c> files it under Errors, and
    /// Message stays the generic "Operation failed" — asserting on Message would pass on any
    /// refusal, which is exactly the match-the-envelope trap.</summary>
    private static string Refusal(ApiResponse<ReservationDto> body) =>
        body.Errors is { Count: > 0 } errors ? errors[0] : string.Empty;

    private object Payload(Guid primaryId, Guid[]? combined, int guests, string start = "18:00:00", string end = "20:00:00") => new
    {
        customerName = "#561 guest",
        customerEmail = "561@example.com",
        customerPhone = (string?)null,
        tableId = primaryId,
        combinedTableIds = combined,
        reservationDate = $"{BookedDay}T00:00:00Z",
        startTime = start,
        endTime = end,
        numberOfGuests = guests,
        specialRequests = (string?)null,
    };

    [Fact]
    public async Task A_party_beyond_any_single_table_books_ONE_reservation_over_three_tables()
    {
        var body = await CreateAsync(Payload(_t1, new[] { _t2, _t3 }, 10));

        // Written as the affirmative string, not the status code: a refusal also arrives as
        // HTTP 200 with success:false.
        body.Success.Should().BeTrue(string.Join("; ", body.Errors ?? new List<string>()));

        body.Data!.TableId.Should().Be(_t1);
        body.Data.CombinedTableIds.Should().Equal(new[] { _t2, _t3 });

        // The claim is about PERSISTENCE, not the mapped response: the child rows must exist.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await context.Reservations.Include(r => r.CombinedTables)
            .SingleAsync(r => r.Id == body.Data.Id);
        stored.CombinedTables.Select(c => c.TableId).Should().Equal(new[] { _t2, _t3 });
    }

    [Fact]
    public async Task The_slot_refuses_a_second_booking_on_any_combined_table()
    {
        (await CreateAsync(Payload(_t1, new[] { _t2, _t3 }, 10))).Success.Should().BeTrue();

        // 19:00 overlaps the combined sitting on every member table — T2 is the interesting one.
        var body = await CreateAsync(Payload(_t2, null, 2, start: "19:00:00", end: "21:00:00"));

        body.Success.Should().BeFalse("T2 is occupied by the combined booking's CHILD row");
        Refusal(body).Should().Contain("not available");
    }

    [Fact]
    public async Task A_party_beyond_the_combined_capacity_is_refused()
    {
        var body = await CreateAsync(Payload(_t1, new[] { _t2, _t3 }, 13));

        body.Success.Should().BeFalse("the set seats 12 in total");
        Refusal(body).Should().Contain("12 guests in total");
    }

    [Fact]
    public async Task The_combined_list_may_not_repeat_the_primary_table()
    {
        var body = await CreateAsync(Payload(_t1, new[] { _t1, _t2 }, 6));

        body.Success.Should().BeFalse();
    }

    [Fact]
    public async Task The_combined_list_may_not_repeat_itself()
    {
        var body = await CreateAsync(Payload(_t1, new[] { _t2, _t2 }, 6));

        body.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Available_slots_show_the_combined_booking_occupying_every_table()
    {
        (await CreateAsync(Payload(_t1, new[] { _t2, _t3 }, 10))).Success.Should().BeTrue();

        var response = await Client.GetAsync($"/api/reservations/available-slots?date={BookedDay}&numberOfGuests=2");
        var body = await ReadResponseAsync<ApiResponse<AvailableTimeSlotsDto>>(response);
        body!.Success.Should().BeTrue(string.Join("; ", body.Errors ?? new List<string>()));

        var slot1800 = body.Data!.TimeSlots.Single(s => s.StartTime == new TimeSpan(18, 0, 0));
        var occupied = slot1800.AvailableTables.Select(t => t.Id).Should().NotContain(new[] { _t1, _t2, _t3 });
        slot1800.AvailableTables.Should().ContainSingle(t => t.Id == _t4);

        // After the sitting ends the tables come back — the occupancy is the SLOT's, not the day's.
        var slot2030 = body.Data.TimeSlots.Single(s => s.StartTime == new TimeSpan(20, 30, 0));
        slot2030.AvailableTables.Should().Contain(t => t.Id == _t1);
    }
}
