using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

// GDPR storage-limitation (Art. 5(1)(e)). A reservation is not a financial record,
// so ReservationRetentionService anonymizes the contact snapshot on reservations
// older than the configured window while retaining the row (table/date/guests) for
// occupancy history. Data-loss class — DISABLED unless ReservationRetention:Enabled.
[Collection("Database Lane 2")]
public class ReservationRetentionServiceTests : IntegrationTestBase
{
    public ReservationRetentionServiceTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    private static ReservationRetentionService BuildService(IServiceProvider services, bool enabled, int retentionMonths = 24)
        => new(
            services,
            NullLogger<ReservationRetentionService>.Instance,
            Options.Create(new ReservationRetentionSettings { Enabled = enabled, RetentionMonths = retentionMonths }));

    [Fact]
    public async Task ScrubExpiredReservations_AnonymizesOldRows_PreservesRecentRowsAndAccountLink()
    {
        var userId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var recentId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestUserSeeder.SeedUserAsync(db, userId, "old-guest@example.com");

            var table = new Table { TableNumber = "T-RET", MaxGuests = 4, CreatedBy = "test" };
            db.Tables.Add(table);

            // Older than the 24-month window → must be scrubbed. Linked to an account
            // so we can assert the CustomerId link is preserved (account lifecycle owns it).
            db.Reservations.Add(new Reservation
            {
                Id = oldId,
                CustomerId = userId,
                CustomerName = "Ada Lovelace",
                CustomerEmail = "ada@example.com",
                CustomerPhone = "+41791112233",
                SpecialRequests = "Window seat",
                Notes = "regular customer Ada",
                Table = table,
                ReservationDate = DateTime.UtcNow.AddMonths(-25),
                StartTime = TimeSpan.FromHours(19),
                EndTime = TimeSpan.FromHours(21),
                NumberOfGuests = 2,
                Status = ReservationStatus.Completed,
                CreatedBy = "test",
            });

            // Within the window → must be left untouched.
            db.Reservations.Add(new Reservation
            {
                Id = recentId,
                CustomerName = "Grace Hopper",
                CustomerEmail = "grace@example.com",
                CustomerPhone = "+41794445566",
                SpecialRequests = "High chair",
                Notes = "first visit",
                Table = table,
                ReservationDate = DateTime.UtcNow.AddMonths(-1),
                StartTime = TimeSpan.FromHours(18),
                EndTime = TimeSpan.FromHours(20),
                NumberOfGuests = 3,
                Status = ReservationStatus.Confirmed,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        var service = BuildService(Factory.Services, enabled: true);
        var firstRun = await service.ScrubExpiredReservations(CancellationToken.None);
        firstRun.Should().BeGreaterThan(0, "the out-of-window reservation is scrubbed on the first pass");
        // Idempotency: with the backlog drained, the `!= Erased` filter matches nothing.
        var secondRun = await service.ScrubExpiredReservations(CancellationToken.None);
        secondRun.Should().Be(0, "already-scrubbed rows are excluded, so a re-run is a no-op");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var old = await db.Reservations.FirstAsync(r => r.Id == oldId);
            old.CustomerName.Should().Be("[erased]");
            old.CustomerEmail.Should().Be("[erased]");
            old.CustomerPhone.Should().Be("[erased]");
            old.SpecialRequests.Should().BeNull();
            old.Notes.Should().BeNull();
            old.CustomerId.Should().Be(userId, "the account link is owned by the account lifecycle, not retention");
            old.NumberOfGuests.Should().Be(2, "non-PII occupancy history is retained");
            old.Status.Should().Be(ReservationStatus.Completed);

            var recent = await db.Reservations.FirstAsync(r => r.Id == recentId);
            recent.CustomerName.Should().Be("Grace Hopper");
            recent.CustomerEmail.Should().Be("grace@example.com");
            recent.CustomerPhone.Should().Be("+41794445566");
            recent.SpecialRequests.Should().Be("High chair");
            recent.Notes.Should().Be("first visit");
        }
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_ScrubsNothing()
    {
        var oldId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var table = new Table { TableNumber = "T-OFF", MaxGuests = 2, CreatedBy = "test" };
            db.Tables.Add(table);
            db.Reservations.Add(new Reservation
            {
                Id = oldId,
                CustomerName = "Alan Turing",
                CustomerEmail = "alan@example.com",
                CustomerPhone = "+41797778899",
                Table = table,
                ReservationDate = DateTime.UtcNow.AddMonths(-25), // old enough to scrub if enabled
                StartTime = TimeSpan.FromHours(12),
                EndTime = TimeSpan.FromHours(13),
                NumberOfGuests = 1,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        // Enabled=false → ExecuteAsync logs "disabled" and returns immediately, never looping.
        var service = BuildService(Factory.Services, enabled: false);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.Reservations.FirstAsync(r => r.Id == oldId);
            reservation.CustomerName.Should().Be("Alan Turing", "the sweeper is disabled and must not touch any row");
            reservation.CustomerEmail.Should().Be("alan@example.com");
        }
    }
}
