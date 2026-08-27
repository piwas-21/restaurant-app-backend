using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// GET /api/reservations?date=YYYY-MM-DD — the restaurant dashboard's day view (backend #418).
/// </summary>
/// <remarks>
/// These go through REAL HTTP on purpose. The defect was in MODEL BINDING: a bare calendar day
/// bound to a <c>DateTime</c> with <see cref="DateTimeKind.Unspecified"/>, which Npgsql refuses to
/// compare with the <c>timestamptz</c> column — so the handler threw and answered
/// <c>success:false</c> for every dated call. A unit test handing the handler a UTC
/// <see cref="DateTime"/> it built itself cannot reproduce that: it never binds anything.
/// </remarks>
[Collection("Database Lane 2")]
public class GetReservationsDateFilterTests : IntegrationTestBase
{
    public GetReservationsDateFilterTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>The day under test, as an operator would name it.</summary>
    private const string TargetDay = "2026-04-15";

    private static readonly DateTime TargetDayMidnightUtc =
        new(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var table = new Table { TableNumber = "T-418", MaxGuests = 4, CreatedBy = "test" };
        context.Tables.Add(table);

        context.Reservations.AddRange(
            // Exactly the day's first instant — the boundary the half-open window must INCLUDE.
            Booking("day-midnight", table, TargetDayMidnightUtc, TimeSpan.FromHours(12)),
            // Late on the same day: same stored day, latest sitting.
            Booking("day-late", table, TargetDayMidnightUtc, TimeSpan.FromHours(23)),
            // The last instant BEFORE the day begins — must be excluded, and would not be by a
            // window built with a stray hour of offset.
            Booking("prev-day-last-instant", table, TargetDayMidnightUtc.AddTicks(-1), TimeSpan.FromHours(23)),
            // The first instant of the NEXT day — the half-open upper bound must EXCLUDE it.
            Booking("next-day-midnight", table, TargetDayMidnightUtc.AddDays(1), TimeSpan.FromHours(12)));

        await context.SaveChangesAsync();
    }

    private static Reservation Booking(string name, Table table, DateTime reservationDate, TimeSpan startTime) => new()
    {
        Id = Guid.NewGuid(),
        CustomerName = name,
        CustomerEmail = $"{name}@example.com",
        // Set even though the entity allows null: EF maps the column IsRequired, so a NULL row
        // makes the projection throw on materialisation — a different fault, not this one.
        CustomerPhone = "+41790000000",
        Table = table,
        ReservationDate = reservationDate,
        StartTime = startTime,
        EndTime = startTime.Add(TimeSpan.FromHours(1)),
        NumberOfGuests = 2,
        Status = ReservationStatus.Confirmed,
        CreatedBy = "test",
    };

    [Fact]
    public async Task BareCalendarDay_Succeeds_AndReturnsOnlyThatDaysBookings()
    {
        AuthenticateAsAdmin();

        var result = await GetReservations($"/api/reservations?date={TargetDay}&pageSize=50");

        result!.Items.Select(r => r.CustomerName)
            .Should().BeEquivalentTo(new[] { "day-midnight", "day-late" });
    }

    [Fact]
    public async Task DayWindow_IsHalfOpen_AtBothMidnights()
    {
        AuthenticateAsAdmin();

        var previousDay = await GetReservations("/api/reservations?date=2026-04-14&pageSize=50");
        var nextDay = await GetReservations("/api/reservations?date=2026-04-16&pageSize=50");

        previousDay!.Items.Select(r => r.CustomerName).Should().BeEquivalentTo(new[] { "prev-day-last-instant" });
        nextDay!.Items.Select(r => r.CustomerName).Should().BeEquivalentTo(new[] { "next-day-midnight" });
    }

    [Fact]
    public async Task NoDateFilter_ReturnsEveryBooking()
    {
        AuthenticateAsAdmin();

        var result = await GetReservations("/api/reservations?pageSize=50");

        result!.Items.Select(r => r.CustomerName).Should().Contain(
            new[] { "day-midnight", "day-late", "prev-day-last-instant", "next-day-midnight" });
    }

    [Fact]
    public async Task UnparseableDate_Returns400_NotAFailedQuery()
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync("/api/reservations?date=not-a-date");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>GET the endpoint and assert the envelope reports success before unwrapping it.</summary>
    private async Task<PagedResult<ReservationDto>?> GetReservations(string url)
    {
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<PagedResult<ReservationDto>>>(json, JsonOptions);
        envelope.Should().NotBeNull();

        // The bug's own signature: HTTP 200 carrying success:false and "Failed to retrieve
        // reservations", because the handler caught the Npgsql ArgumentException.
        envelope!.Success.Should().BeTrue(because: "a dated query must not fail: {0}", envelope.Message);
        return envelope.Data;
    }
}
