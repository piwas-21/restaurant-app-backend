using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

// GDPR Art. 17 (right to erasure). AccountCleanupService hard-deletes a user
// after the deletion grace period AND must scrub the denormalized contact
// snapshots on the retained business records (orders/reservations/addresses) —
// previously it only nulled the FK, leaving name/email/phone/address behind.
public class AccountCleanupServiceTests : IntegrationTestBase
{
    public AccountCleanupServiceTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task ProcessDeletionRequests_ScrubsContactSnapshots_AndDeletesUser()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestUserSeeder.SeedUserAsync(db, userId, "erase-me@example.com");

            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.DeletionScheduledAt = DateTime.UtcNow.AddDays(-1); // grace elapsed → eligible

            db.Orders.Add(new Order
            {
                Id = orderId,
                OrderNumber = $"ERZ-{orderId:N}"[..16],
                UserId = userId,
                Type = OrderType.Takeaway,
                Status = OrderStatus.Completed,
                PaymentStatus = PaymentStatus.Completed,
                SubTotal = 20m,
                Total = 20m,
                OrderDate = DateTime.UtcNow,
                CreatedBy = "test",
                CustomerName = "Ada Lovelace",
                CustomerEmail = "ada@example.com",
                CustomerPhone = "+41791112233",
                Notes = "call Ada on 0791112233",
                CancellationReason = "Ada changed her mind",
                Focus = new OrderFocus { FocusedAt = DateTime.UtcNow, Reason = "VIP Ada" },
            });
            db.OrderAddresses.Add(new OrderAddress
            {
                OrderId = orderId,
                Label = "Home",
                AddressLine1 = "1 Rue Test",
                AddressLine2 = "Apt 4",
                City = "Geneva",
                State = "GE",
                PostalCode = "1200",
                Country = "CH",
                Phone = "+41791112233",
                Latitude = 46.2044,
                Longitude = 6.1432,
                DeliveryInstructions = "Ring twice",
                CreatedBy = "test",
            });

            var table = new Table { TableNumber = "T-QA", MaxGuests = 4, CreatedBy = "test" };
            db.Tables.Add(table);
            db.Reservations.Add(new Reservation
            {
                Id = reservationId,
                CustomerId = userId,
                CustomerName = "Ada Lovelace",
                CustomerEmail = "ada@example.com",
                CustomerPhone = "+41791112233",
                SpecialRequests = "Window seat",
                Notes = "regular customer Ada",
                Table = table,
                ReservationDate = DateTime.UtcNow.Date,
                StartTime = TimeSpan.FromHours(19),
                EndTime = TimeSpan.FromHours(21),
                NumberOfGuests = 2,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        var service = new AccountCleanupService(Factory.Services, NullLogger<AccountCleanupService>.Instance);
        await service.ProcessDeletionRequests(CancellationToken.None);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId))
                .Should().BeFalse("the user row is hard-deleted");

            var order = await db.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == orderId);
            order.UserId.Should().BeNull();
            order.CustomerName.Should().BeNull();
            order.CustomerEmail.Should().BeNull();
            order.CustomerPhone.Should().BeNull();
            order.Notes.Should().BeNull();
            order.CancellationReason.Should().BeNull();
            order.Focus!.Reason.Should().BeNull();
            order.Focus.FocusedAt.Should().NotBe(default, "erasure scrubs the reason, not the focus itself");
            order.Total.Should().Be(20m, "the financial record is retained");

            var address = await db.OrderAddresses.FirstAsync(a => a.OrderId == orderId);
            address.Label.Should().Be("[erased]");
            address.AddressLine1.Should().Be("[erased]");
            address.City.Should().Be("[erased]");
            address.PostalCode.Should().Be("[erased]");
            address.Country.Should().Be("[erased]");
            address.AddressLine2.Should().BeNull();
            address.State.Should().BeNull();
            address.Phone.Should().BeNull();
            address.Latitude.Should().BeNull();
            address.Longitude.Should().BeNull();
            address.DeliveryInstructions.Should().BeNull();

            var reservation = await db.Reservations.FirstAsync(r => r.Id == reservationId);
            reservation.CustomerId.Should().BeNull();
            reservation.CustomerName.Should().Be("[erased]");
            reservation.CustomerEmail.Should().Be("[erased]");
            reservation.CustomerPhone.Should().Be("[erased]");
            reservation.SpecialRequests.Should().BeNull();
            reservation.Notes.Should().BeNull();
        }
    }
}
