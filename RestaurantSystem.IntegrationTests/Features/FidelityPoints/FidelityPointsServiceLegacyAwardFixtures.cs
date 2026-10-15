using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
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
        var now = DateTime.UtcNow;
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Pre-loyalty migration item",
            Quantity = quantity,
            UnitPrice = orderTotal / quantity,
            ItemTotal = orderTotal,
            CreatedAt = now,
            CreatedBy = "PreLoyaltyMigrationFixture"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"TEST-{orderId:N}"[..16],
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = orderTotal,
            Total = orderTotal,
            RemainingAmount = orderTotal,
            FidelityPointsEarned = candidatePoints,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = "PreLoyaltyMigrationFixture",
            Items = [item]
        };
        await LegacyEntityFixture.InsertAsync(context, order);
        await LegacyEntityFixture.InsertAsync(
            context,
            item,
            "section_id",
            "composition_role",
            "configuration_scope",
            "menu_section_item_id",
            "parent_component_order_item_id",
            "presentation_label",
            "presentation_order",
            "quantity_basis",
            "suggested_side_item_id",
            "preparation_time_minutes_snapshot");

        var rule = new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Historical loyalty rule", 0m,
            null, candidatePoints, 1);
        var evaluation = new OrderBillingEarningEvaluation(candidatePoints, "fixed-priority-v1",
            new string('a', 64), rule);
        var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", evaluation, null,
            OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows);
        await using var snapshotTransaction = await context.Database.BeginTransactionAsync();
        await LegacyOrderBillingSnapshotFixture.InsertAsync(context, snapshot.Header);
        context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        await context.SaveChangesAsync();
        await snapshotTransaction.CommitAsync();
        return orderId;
    }
}
