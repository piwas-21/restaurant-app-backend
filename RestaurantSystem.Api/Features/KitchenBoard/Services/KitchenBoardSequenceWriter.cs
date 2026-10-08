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
        await KitchenBoardSequenceLock.AcquireAsync(context, cancellationToken);
    }

    internal static Task<int> TouchCorrectionsAsync(
        ApplicationDbContext context,
        IEnumerable<(Guid OrderId, Guid WorkItemId, DevicePrintTarget Target)> corrections,
        CancellationToken cancellationToken)
    {
        var batch = corrections.Distinct().ToArray();
        if (batch.Length == 0)
            return Task.FromResult(0);

        var orderIds = batch.Select(item => item.OrderId).ToArray();
        var workItemIds = batch.Select(item => item.WorkItemId).ToArray();
        var targets = batch.Select(item => item.Target.ToString()).ToArray();

        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "OrderOperationalNotes" AS note
            SET kitchen_board_sequence = next_kitchen_board_change_sequence()
            FROM unnest({orderIds}::uuid[], {workItemIds}::uuid[], {targets}::text[])
                AS changed(order_id, work_item_id, target)
            WHERE note.id = changed.work_item_id
                AND note.order_id = changed.order_id
                AND note.audience = 'Kitchen'
                AND note.kitchen_target = changed.target::varchar(20)
            """,
            cancellationToken);
    }

    internal static Task<int> TouchCorrectionAsync(
        ApplicationDbContext context,
        Guid orderId,
        Guid workItemId,
        DevicePrintTarget target,
        CancellationToken cancellationToken) => TouchCorrectionsAsync(
            context, [(orderId, workItemId, target)], cancellationToken);
}
