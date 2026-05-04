using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Helper for seeding minimal <see cref="Order"/> rows that other entities can FK to.
///
/// FidelityPointsTransaction.OrderId — though typed as <c>Guid?</c> on the entity —
/// is enforced by a FK constraint in Postgres. Tests that mint a fresh
/// <c>Guid.NewGuid()</c> as the order ID and persist a transaction must seed a
/// corresponding row in <c>orders</c>; otherwise <c>SaveChangesAsync</c> fails
/// with 23503 (foreign key violation).
///
/// The seeded order is minimal (Pending status, no items, no FKs to other tables)
/// and idempotent via <c>FindAsync</c>, so callers don't need to reason about
/// duplicate inserts within a test.
/// </summary>
public static class TestOrderSeeder
{
    public static async Task SeedOrderAsync(ApplicationDbContext context, Guid orderId, Guid? userId = null)
    {
        if (await context.Orders.FindAsync(orderId) is not null)
        {
            return;
        }

        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"TEST-{orderId:N}".Substring(0, 16),
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 0m,
            Total = 0m,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestOrderSeeder",
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }
}
