using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardCompletionStream
{
    private const string StreamName = "completions";

    internal static async Task<KitchenBoardPageDto<KitchenBoardCompletionDto>> ReadAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        string? cursorValue,
        int pageSize,
        long currentWatermark,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursorValue))
        {
            var count = await ActiveInitialCompletions(context).CountAsync(cancellationToken);
            return await SnapshotAsync(context, cursor, filterHash, currentWatermark,
                pageSize, count, null, cancellationToken);
        }

        var payload = KitchenBoardCursorPolicy.Read(cursor, cursorValue, filterHash, StreamName, pageSize);
        if (payload.Mode == OperationalQueueSyncModes.Snapshot)
        {
            return await SnapshotAsync(context, cursor, filterHash, payload.UpperSequence,
                pageSize, payload.TotalCount, payload.Position, cancellationToken, payload.Page);
        }

        return await ChangesAsync(context, cursor, filterHash, payload, pageSize,
            currentWatermark, cancellationToken);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCompletionDto>> SnapshotAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        long upper,
        int pageSize,
        int totalCount,
        string? afterSequence,
        CancellationToken cancellationToken,
        int page = 1)
    {
        var query = ActiveInitialCompletions(context)
            .Where(value => value.Sequence <= upper);
        if (afterSequence is not null)
        {
            if (!long.TryParse(afterSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
                || position < 0)
            {
                throw KitchenBoardCursorPolicy.InvalidCursor();
            }

            query = query.Where(value => value.Sequence > position);
        }

        var rows = await query.OrderBy(value => value.Sequence)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var items = rows.Take(pageSize).Select(Map).ToList();
        var hasMore = rows.Count > pageSize;
        var next = hasMore && items.Count > 0
            ? KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
                OperationalQueueSyncModes.Snapshot, upper, 0,
                items[^1].Sequence.ToString(CultureInfo.InvariantCulture), rows[pageSize - 1].Id,
                page + 1, pageSize, totalCount)
            : WatermarkCursor(cursor, filterHash, upper, pageSize, totalCount);
        return new KitchenBoardPageDto<KitchenBoardCompletionDto>(
            items, totalCount, hasMore, [], next, upper, OperationalQueueSyncModes.Snapshot);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCompletionDto>> ChangesAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        OperationalQueueCursorPayload payload,
        int pageSize,
        long currentWatermark,
        CancellationToken cancellationToken)
    {
        var lower = payload.Mode == OperationalQueueSyncModes.Watermark
            ? payload.UpperSequence : payload.LowerSequence;
        var upper = payload.Mode == OperationalQueueSyncModes.Watermark
            ? currentWatermark : payload.UpperSequence;
        if (upper < lower) throw KitchenBoardCursorPolicy.InvalidCursor();

        var rowsQuery = context.KitchenBoardWorkCompletions.AsNoTracking()
            .Where(value => value.Sequence > lower && value.Sequence <= upper);
        if (payload.Mode == OperationalQueueSyncModes.Changes)
        {
            var afterSequence = KitchenBoardCursorPolicy.ParsePosition(payload);
            rowsQuery = rowsQuery.Where(value => value.Sequence > afterSequence);
        }

        var rows = await rowsQuery.OrderBy(value => value.Sequence)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var items = rows.Take(pageSize).Select(Map).ToList();
        var count = await ActiveInitialCompletions(context).CountAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var next = hasMore
            ? KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
                OperationalQueueSyncModes.Changes, upper, lower,
                rows[pageSize - 1].Sequence.ToString(CultureInfo.InvariantCulture), null,
                payload.Page + 1, pageSize, count)
            : WatermarkCursor(cursor, filterHash, upper, pageSize, count);
        return new KitchenBoardPageDto<KitchenBoardCompletionDto>(
            items, count, hasMore, [], next, upper,
            hasMore ? OperationalQueueSyncModes.Changes : OperationalQueueSyncModes.Watermark);
    }

    private static KitchenBoardCompletionDto Map(KitchenBoardWorkCompletion value) => new(
        value.OrderId,
        value.WorkItemId,
        value.Kind.ToString(),
        value.AccountRevision,
        value.AcknowledgedOrderVersion,
        value.Sequence,
        value.CreatedAt);

    private static IQueryable<KitchenBoardWorkCompletion> ActiveInitialCompletions(
        ApplicationDbContext context) => context.KitchenBoardWorkCompletions.AsNoTracking()
        .Where(value => value.Kind == KitchenBoardWorkKind.InitialOrder
            && value.WorkItemId == value.OrderId
            && value.Order.IsKitchenReleased && value.Order.ExternalReference == null
            && !value.Order.IsDeleted && value.Order.Status == OrderStatus.Ready);

    private static string WatermarkCursor(
        IOperationalQueueCursor cursor, string filterHash, long upper, int pageSize, int totalCount) =>
        KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
            OperationalQueueSyncModes.Watermark, upper, 0, null, null, 1, pageSize, totalCount);
}
