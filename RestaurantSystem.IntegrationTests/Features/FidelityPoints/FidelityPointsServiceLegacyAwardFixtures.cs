using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    private static async Task<Guid> SeedLegacyAcceptedAwardOrderAsync(
        ApplicationDbContext context,
        Guid userId,
        int candidatePoints,
        decimal orderTotal,
        int quantity = 1)
    {
        if (!await context.Users.AnyAsync(user => user.Id == userId))
            await TestUserSeeder.SeedUserAsync(context, userId);

        var orderId = Guid.NewGuid();
        await TestOrderSeeder.SeedOrderAsync(context, orderId, userId);
        var order = await context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Completed;
        order.SubTotal = orderTotal;
        order.Total = orderTotal;
        order.FidelityPointsEarned = candidatePoints;
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Pre-loyalty migration item",
            Quantity = quantity,
            UnitPrice = orderTotal / quantity,
            ItemTotal = orderTotal,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "PreLoyaltyMigrationFixture"
        };
        order.Items.Add(item);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        var rule = new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Historical loyalty rule", 0m,
            null, candidatePoints, 1);
        var evaluation = new OrderBillingEarningEvaluation(candidatePoints, "fixed-priority-v1",
            new string('a', 64), rule);
        var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", evaluation, null,
            OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows);
        context.OrderBillingSnapshots.Add(snapshot.Header);
        context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        await context.SaveChangesAsync();
        return orderId;
    }
}
