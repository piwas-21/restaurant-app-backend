using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// <c>PUT /api/Reservations/{id}/mine</c> — the guest-safe edit route (mobile BACKEND-NOTES item 1).
/// Before it existed a customer's only write paths were create and cancel, and the admin
/// <c>PUT /api/Reservations/{id}</c> answered them 403.
/// </summary>
/// <remarks>
/// Two things are pinned here that a "does it save?" test would not catch: that the route cannot be
/// used to reach ANOTHER guest's booking (it answers 404, never 403 — a 403 would confirm the id
/// exists), and that <c>status</c> / <c>tableId</c> smuggled into the body change nothing, which is
/// the whole reason this is not the admin DTO.
/// <para>
/// Every booked day is 2030, so the tenant clock needs no double: the class asserts authorization
/// and edit rules, not the clock (<c>ReservationClockTests</c> owns that).
/// </para>
/// </remarks>
[Collection("Database Lane 1")]
public class UpdateMyReservationTests : IntegrationTestBase
{
    private static readonly Guid MyTableId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherTableId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OtherUserId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid CallerId = Guid.Parse(TestAuthHandler.UserId);

    /// <summary>Friday 2030-05-17, as the wire spells a calendar day.</summary>
    private const string BookedDay = "2030-05-17T00:00:00Z";
    private const string MovedDay = "2030-05-18T00:00:00Z";

    public UpdateMyReservationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await TestUserSeeder.SeedUserAsync(db, OtherUserId, "other-guest@example.com");

        db.Tables.Add(new Table { Id = MyTableId, TableNumber = "T-MINE", MaxGuests = 4, CreatedBy = "test" });
        db.Tables.Add(new Table { Id = OtherTableId, TableNumber = "T-OTHER", MaxGuests = 8, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_guest_can_move_their_own_booking()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(
            date: MovedDay, start: "20:00:00", end: "22:00:00", guests: 4,
            name: "Ada Lovelace", phone: "+41791112233", requests: "Window seat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var saved = await LoadAsync(id);
        saved.ReservationDate.Date.Should().Be(new DateTime(2030, 5, 18));
        saved.StartTime.Should().Be(new TimeSpan(20, 0, 0));
        saved.EndTime.Should().Be(new TimeSpan(22, 0, 0));
        saved.NumberOfGuests.Should().Be(4);
        saved.CustomerName.Should().Be("Ada Lovelace");
        saved.CustomerPhone.Should().Be("+41791112233");
        saved.SpecialRequests.Should().Be("Window seat");
        saved.Status.Should().Be(ReservationStatus.Pending);
        saved.TableId.Should().Be(MyTableId);
    }

    [Fact]
    public async Task Another_guests_booking_answers_not_found_and_is_left_alone()
    {
        var id = await SeedReservationAsync(OtherUserId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        // 404 and not 403: a distinct status would confirm the id exists.
        await AssertRefusedAsync(response, HttpStatusCode.NotFound, ErrorCodes.ReservationNotFound);
        (await LoadAsync(id)).NumberOfGuests.Should().Be(2, "the other guest's booking must be untouched");
    }

    [Fact]
    public async Task A_guest_booking_with_no_owner_is_not_editable_by_a_signed_in_user()
    {
        // CustomerId == null. Matching a null owner against the caller would hand every walk-in
        // booking to any signed-in customer.
        var id = await SeedReservationAsync(customerId: null, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        await AssertRefusedAsync(response, HttpStatusCode.NotFound, ErrorCodes.ReservationNotFound);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsAnonymous();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cancelled_booking_can_no_longer_be_changed()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Cancelled);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        await AssertRefusedAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ReservationNotEditable);
        (await LoadAsync(id)).Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task A_booking_whose_day_has_passed_can_no_longer_be_changed()
    {
        var id = await SeedReservationAsync(
            CallerId, ReservationStatus.Confirmed, day: new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        await AssertRefusedAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ReservationNotEditable);
    }

    [Fact]
    public async Task Moving_a_live_booking_into_the_past_is_refused()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync(
            $"/api/Reservations/{id}/mine", Body(date: "2020-01-15T00:00:00Z", guests: 2));

        await AssertRefusedAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ReservationDateInPast);
    }

    [Fact]
    public async Task A_party_larger_than_the_assigned_table_is_refused_rather_than_re_seated()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        // T-MINE seats 4; T-OTHER seats 8 and is deliberately NOT chosen for the guest.
        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 6));

        await AssertRefusedAsync(
            response, HttpStatusCode.BadRequest, ErrorCodes.ReservationTableCapacityExceeded);

        var saved = await LoadAsync(id);
        saved.NumberOfGuests.Should().Be(2);
        saved.TableId.Should().Be(MyTableId);
    }

    [Fact]
    public async Task A_time_that_overlaps_another_live_booking_on_the_same_table_is_refused()
    {
        var mine = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        await SeedReservationAsync(
            OtherUserId, ReservationStatus.Confirmed, start: new TimeSpan(20, 0, 0), end: new TimeSpan(22, 0, 0));
        AuthenticateAsUser();

        var response = await PutAsJsonAsync(
            $"/api/Reservations/{mine}/mine", Body(start: "21:00:00", end: "23:00:00", guests: 2));

        await AssertRefusedAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ReservationSlotUnavailable);
    }

    [Fact]
    public async Task Status_and_tableId_in_the_body_change_nothing()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", new
        {
            customerName = "Grace Hopper",
            customerEmail = "grace@example.com",
            customerPhone = "+41794445566",
            reservationDate = BookedDay,
            startTime = "19:00:00",
            endTime = "21:00:00",
            numberOfGuests = 2,
            // The two fields this endpoint exists to keep out of a guest's hands.
            status = "Confirmed",
            tableId = OtherTableId,
            notes = "please seat us in the VIP room",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var saved = await LoadAsync(id);
        saved.Status.Should().Be(ReservationStatus.Pending, "a guest cannot approve their own booking");
        saved.TableId.Should().Be(MyTableId, "a guest cannot move themselves onto another table");
        saved.Notes.Should().BeNull("notes are the restaurant's, not the guest's");
    }

    [Fact]
    public async Task Reshaping_a_confirmed_booking_sends_it_back_for_approval()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Confirmed);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", Body(guests: 4));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await LoadAsync(id)).Status.Should().Be(
            ReservationStatus.Pending, "the restaurant approved 2 guests, not 4");
    }

    [Fact]
    public async Task Fixing_a_typo_in_the_contact_details_keeps_a_confirmed_booking_confirmed()
    {
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Confirmed);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync(
            $"/api/Reservations/{id}/mine", Body(name: "Grace B. Hopper", guests: 2));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var saved = await LoadAsync(id);
        saved.Status.Should().Be(ReservationStatus.Confirmed, "the booked shape did not change");
        saved.CustomerName.Should().Be("Grace B. Hopper");
    }

    [Fact]
    public async Task A_reservation_date_that_is_not_midnight_is_refused_loudly()
    {
        // A client sending its own local midnight with an offset parses to the PREVIOUS day on the
        // server. Refusing it beats silently moving the booking a day back.
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync(
            $"/api/Reservations/{id}/mine", Body(date: "2030-05-18T19:30:00Z", guests: 2));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("midnight");
    }

    [Fact]
    public async Task A_midnight_date_without_a_Z_suffix_still_books_that_very_day()
    {
        // "2030-05-18T00:00:00" parses to DateTimeKind.Unspecified, which Npgsql REFUSES as a
        // parameter against a timestamptz column — the conflict query would have thrown a 500 had
        // the handler passed the client's value through instead of stamping it UTC first.
        var id = await SeedReservationAsync(CallerId, ReservationStatus.Pending);
        AuthenticateAsUser();

        var response = await PutAsJsonAsync(
            $"/api/Reservations/{id}/mine", Body(date: "2030-05-18T00:00:00", guests: 2));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await LoadAsync(id)).ReservationDate.Date.Should().Be(new DateTime(2030, 5, 18));
    }

    private static object Body(
        string date = BookedDay,
        string start = "19:00:00",
        string end = "21:00:00",
        int guests = 2,
        string name = "Grace Hopper",
        string? phone = "+41794445566",
        string? requests = null) => new
        {
            customerName = name,
            customerEmail = "grace@example.com",
            customerPhone = phone,
            reservationDate = date,
            startTime = start,
            endTime = end,
            numberOfGuests = guests,
            specialRequests = requests,
        };

    private async Task<Guid> SeedReservationAsync(
        Guid? customerId,
        ReservationStatus status,
        DateTime? day = null,
        TimeSpan? start = null,
        TimeSpan? end = null)
    {
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Reservations.Add(new Reservation
        {
            Id = id,
            CustomerId = customerId,
            CustomerName = "Grace Hopper",
            CustomerEmail = "grace@example.com",
            CustomerPhone = "+41794445566",
            TableId = MyTableId,
            ReservationDate = day ?? new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = start ?? new TimeSpan(19, 0, 0),
            EndTime = end ?? new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = status,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();

        return id;
    }

    private async Task<Reservation> LoadAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    /// <summary>
    /// Asserts the status AND the camelCase <c>errorCode</c> on the RAW body: the exception path
    /// builds its own serializer options, so a deserialize-only assertion would pass on any
    /// spelling (the test reader is case-insensitive).
    /// </summary>
    private static async Task AssertRefusedAsync(
        HttpResponseMessage response, HttpStatusCode expected, string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("errorCode").GetString().Should().Be(expectedCode);
    }
}
