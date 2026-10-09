using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
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
        KitchenBoardStreamReadContext request)
    {
        var context = request.Context;
        var cancellationToken = request.CancellationToken;
        if (string.IsNullOrWhiteSpace(request.CursorValue))
        {
            var count = await ActiveInitialCompletions(context).CountAsync(cancellationToken);
            return await SnapshotAsync(request, request.CurrentWatermark, count, null);
        }

        var payload = KitchenBoardCursorPolicy.Read(
            request.Cursor, request.CursorValue, request.FilterHash, StreamName, request.PageSize);
        if (payload.Mode == OperationalQueueSyncModes.Snapshot)
        {
            return await SnapshotAsync(
                request, payload.UpperSequence, payload.TotalCount, payload.Position, payload.Page);
        }

        return await ChangesAsync(request, payload);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCompletionDto>> SnapshotAsync(
        KitchenBoardStreamReadContext request,
        long upper,
        int totalCount,
        string? afterSequence,
        int page = 1)
    {
        var context = request.Context;
        var pageSize = request.PageSize;
        var cancellationToken = request.CancellationToken;
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
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Snapshot,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    Position = items[^1].Sequence.ToString(CultureInfo.InvariantCulture),
                    PositionId = rows[pageSize - 1].Id,
                    Page = page + 1,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                })
            : WatermarkCursor(request, upper, totalCount);
        return new KitchenBoardPageDto<KitchenBoardCompletionDto>(
            items, totalCount, hasMore, [], next, upper, OperationalQueueSyncModes.Snapshot);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCompletionDto>> ChangesAsync(
        KitchenBoardStreamReadContext request,
        OperationalQueueCursorPayload payload)
    {
        var context = request.Context;
        var pageSize = request.PageSize;
        var currentWatermark = request.CurrentWatermark;
        var cancellationToken = request.CancellationToken;
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
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Changes,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    LowerSequence = lower,
                    Position = rows[pageSize - 1].Sequence.ToString(CultureInfo.InvariantCulture),
                    Page = payload.Page + 1,
                    PageSize = pageSize,
                    TotalCount = count,
                })
            : WatermarkCursor(request, upper, count);
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
        KitchenBoardStreamReadContext request, long upper, int totalCount) =>
        KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
            new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Watermark,
                FilterHash = request.FilterHash,
                UpperSequence = upper,
                Page = 1,
                PageSize = request.PageSize,
                TotalCount = totalCount,
            });
}
