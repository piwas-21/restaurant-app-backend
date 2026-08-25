using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// The migration path for alert mails that were already in the restaurant's inbox when link
/// signing shipped (backend #402): they carry no <c>?token=</c> at all, and breaking every one of
/// them on release day would strand whatever bookings were still undecided.
/// </summary>
/// <remarks>
/// <para>
/// The window is anchored on each reservation's own <c>CreatedAt</c>, not on a date somebody has to
/// remember to remove: it closes booking by booking with nothing left to clean up. Its cost is
/// stated plainly by the second test below — while the window is open, the id alone is still enough
/// for a booking that young, which is exactly the hole #402 reported. Two settings close it:
/// <c>LegacyLinkCutoffUtc</c> (recommended, set at release — proven in
/// <c>ReservationQuickActionLinkAuthorizationTests</c>) and <c>LegacyLinkGraceDays: 0</c>.
/// </para>
/// <para>
/// This class deliberately does NOT set a cutoff, so it exercises the window on its own.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class ReservationQuickActionLegacyLinkTests : IntegrationTestBase
{
    private static readonly Guid TableId = Guid.Parse("eeeeeeee-0402-0000-0000-000000000002");

    private const string RefusalMarker = "This link can no longer be used";

    public ReservationQuickActionLegacyLinkTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Tables.Add(new Table { Id = TableId, TableNumber = "T-402L", MaxGuests = 4, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_link_from_a_mail_sent_before_signing_still_works_inside_the_window()
    {
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-1));
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/reservations/{id}/quick-approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Reservation Approved");
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task A_token_less_link_is_refused_once_its_own_booking_outruns_the_window()
    {
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-30));
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/reservations/{id}/quick-approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task A_token_less_link_cannot_be_replayed_after_the_booking_was_decided()
    {
        // Inside the window a legacy link is no more reusable than a signed one.
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-1));
        AuthenticateAsAnonymous();

        (await Client.GetAsync($"/api/reservations/{id}/quick-approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await Client.GetAsync($"/api/reservations/{id}/quick-reject");

        (await replay.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Confirmed, "the booking was already decided");
    }

    private async Task<Guid> SeedReservationAsync(DateTime createdAt)
    {
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Reservations.Add(new Reservation
        {
            Id = id,
            CustomerId = null,
            CustomerName = "Ada Lovelace",
            CustomerEmail = "ada@example.com",
            CustomerPhone = "+41791112233",
            TableId = TableId,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            // The anchor under test. ApplicationDbContext only stamps CreatedAt when it is default.
            CreatedAt = createdAt,
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
