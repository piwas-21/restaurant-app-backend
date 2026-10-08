using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardSequenceWriter
{
    internal static async Task LockReceiptBatchAsync(
        ApplicationDbContext context,
        IEnumerable<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().Order().ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM orders WHERE id = ANY ({ids}) ORDER BY id FOR UPDATE",
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('restaurant-system.order-change-sequence', 0))",
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(664311, 2)", cancellationToken);
    }

    internal static Task<int> TouchCorrectionAsync(
        ApplicationDbContext context,
        Guid orderId,
        Guid workItemId,
        DevicePrintTarget target,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "OrderOperationalNotes"
            SET kitchen_board_sequence = next_kitchen_board_change_sequence()
            WHERE id = {workItemId}
                AND order_id = {orderId}
                AND audience = 'Kitchen'
                AND kitchen_target = {target.ToString()}
            """,
            cancellationToken);
}
