using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// <c>POST /api/Reservations/{id}/cancel</c> is <c>[Authorize]</c>, and until this change it
/// checked nothing else: ANY signed-in customer could cancel ANY reservation by id — a stranger's
/// table, a walk-in booking, anything. The controller even carried a <c>// TODO: enforce non-admins
/// can only cancel their own reservations</c>. Found while shipping the guest edit route
/// (mobile BACKEND-NOTES item 1); same class of bug, so it ships in the same PR.
/// </summary>
/// <remarks>
/// The refusal is deliberately WORD FOR WORD the missing-reservation answer, so the route cannot be
/// used to find out which ids exist. That is also why the "left alone" assertion reads the row back:
/// a shared message makes the status code alone a weak witness.
/// </remarks>
[Collection("Database Lane 1")]
public class CancelReservationOwnershipTests : IntegrationTestBase
{
    private static readonly Guid TableId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid OtherUserId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CallerId = Guid.Parse(TestAuthHandler.UserId);

    public CancelReservationOwnershipTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await TestUserSeeder.SeedUserAsync(db, OtherUserId, "other-canceller@example.com");

        db.Tables.Add(new Table { Id = TableId, TableNumber = "T-CANCEL", MaxGuests = 4, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_customer_cannot_cancel_someone_elses_reservation()
    {
        var id = await SeedReservationAsync(OtherUserId);
        AuthenticateAsUser();

        var response = await Client.PostAsync($"/api/Reservations/{id}/cancel", content: null);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("Reservation not found", "the refusal must not confirm the id exists");
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending, "the booking must stand");
    }

    [Fact]
    public async Task A_customer_cannot_cancel_a_walk_in_booking_that_has_no_owner()
    {
        var id = await SeedReservationAsync(customerId: null);
        AuthenticateAsUser();

        var response = await Client.PostAsync($"/api/Reservations/{id}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task A_customer_can_still_cancel_their_own_reservation()
    {
        var id = await SeedReservationAsync(CallerId);
        AuthenticateAsUser();

        var response = await Client.PostAsync($"/api/Reservations/{id}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task Staff_can_still_cancel_any_reservation()
    {
        var id = await SeedReservationAsync(OtherUserId);
        AuthenticateAsAdmin();

        var response = await Client.PostAsync($"/api/Reservations/{id}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task The_restaurants_own_quick_reject_link_still_works_with_no_caller_at_all()
    {
        // The [AllowAnonymous] link in the admin alert mail is the ONE documented opt-out
        // (EnforceOwnership: false). If the ownership check reached it, the restaurant could no
        // longer reject a booking from its own inbox.
        var id = await SeedReservationAsync(OtherUserId);
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/reservations/{id}/quick-reject");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Cancelled);
    }

    private async Task<Guid> SeedReservationAsync(Guid? customerId)
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
            TableId = TableId,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();

        return id;
    }

    private async Task<ReservationStatus> StatusOfAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id)).Status;
    }
}
