using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardCorrectionStream
{
    private const string StreamName = "corrections";

    internal static async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> ReadAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        string? cursorValue,
        int pageSize,
        long currentWatermark,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursorValue))
        {
            var total = await KitchenNotes(context, currentWatermark).CountAsync(cancellationToken);
            return await SnapshotAsync(context, cursor, filterHash, currentWatermark,
                pageSize, total, null, logger, cancellationToken);
        }

        var payload = KitchenBoardCursorPolicy.Read(cursor, cursorValue, filterHash, StreamName, pageSize);
        if (payload.Mode == OperationalQueueSyncModes.Snapshot)
        {
            return await SnapshotAsync(context, cursor, filterHash, payload.UpperSequence,
                pageSize, payload.TotalCount, payload.PositionId, logger, cancellationToken,
                payload.Page);
        }

        return await ChangesAsync(context, cursor, filterHash, payload, pageSize,
            currentWatermark, logger, cancellationToken);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> SnapshotAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        long upper,
        int pageSize,
        int totalCount,
        Guid? afterId,
        ILogger logger,
        CancellationToken cancellationToken,
        int page = 1)
    {
        var query = KitchenNotes(context, upper);
        if (afterId.HasValue)
        {
            query = query.Where(note => note.Id.CompareTo(afterId.Value) > 0);
        }

        var rows = await LoadNotes(query).OrderBy(note => note.Id)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var items = await ProjectAsync(context, rows.Take(pageSize).ToList(), logger, cancellationToken);
        var hasMore = rows.Count > pageSize;
        var next = hasMore && items.Count > 0
            ? KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
                OperationalQueueSyncModes.Snapshot, upper, 0,
                items[^1].WorkItemId.ToString("D"), items[^1].WorkItemId,
                page + 1, pageSize, totalCount)
            : WatermarkCursor(cursor, filterHash, upper, pageSize, totalCount);
        return new KitchenBoardPageDto<KitchenBoardCorrectionDto>(
            items, totalCount, hasMore, [], next, upper, OperationalQueueSyncModes.Snapshot);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> ChangesAsync(
        ApplicationDbContext context,
        IOperationalQueueCursor cursor,
        string filterHash,
        OperationalQueueCursorPayload payload,
        int pageSize,
        long currentWatermark,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lower = payload.Mode == OperationalQueueSyncModes.Watermark
            ? payload.UpperSequence : payload.LowerSequence;
        var upper = payload.Mode == OperationalQueueSyncModes.Watermark
            ? currentWatermark : payload.UpperSequence;
        if (upper < lower) throw KitchenBoardCursorPolicy.InvalidCursor();

        var notes = ChangedKitchenNotes(context, lower, upper);
        if (payload.Mode == OperationalQueueSyncModes.Changes)
        {
            var afterSequence = KitchenBoardCursorPolicy.ParsePosition(payload);
            notes = notes.Where(note => note.KitchenBoardSequence > afterSequence);
        }

        var rows = await LoadNotes(notes).OrderBy(note => note.KitchenBoardSequence)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var pageRows = rows.Take(pageSize).ToList();
        var projected = await ProjectAsync(context, pageRows, logger, cancellationToken);
        var projectedById = projected.ToDictionary(item => item.WorkItemId);
        var rowsById = pageRows.ToDictionary(note => note.Id);
        var items = projected.Where(item => rowsById.TryGetValue(item.WorkItemId, out var note)
            && IsActiveWork(note) && !item.IsCompleted && !item.Withdrawn).ToList();
        var tombstones = projected.Where(item => item.Withdrawn).ToList();
        var removed = pageRows.Where(note => !projectedById.TryGetValue(note.Id, out var item)
                || !IsActiveWork(note) || item.IsCompleted || item.Withdrawn)
            .Select(note => note.Id).Distinct().ToList();
        items.AddRange(tombstones);
        var count = await KitchenBoardWorkRules.ActiveCorrections(context).CountAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var next = hasMore
            ? KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
                OperationalQueueSyncModes.Changes, upper, lower,
                rows[pageSize - 1].KitchenBoardSequence.ToString(CultureInfo.InvariantCulture),
                null, payload.Page + 1, pageSize, count)
            : WatermarkCursor(cursor, filterHash, upper, pageSize, count);
        return new KitchenBoardPageDto<KitchenBoardCorrectionDto>(
            items, count, hasMore, removed, next, upper,
            hasMore ? OperationalQueueSyncModes.Changes : OperationalQueueSyncModes.Watermark);
    }

    private static IQueryable<OrderOperationalNote> KitchenNotes(
        ApplicationDbContext context, long? upper) => KitchenBoardWorkRules.ActiveCorrections(context)
        .Where(note => !upper.HasValue || note.KitchenBoardSequence <= upper.Value);

    private static IQueryable<OrderOperationalNote> ChangedKitchenNotes(
        ApplicationDbContext context, long lower, long upper) => context.OrderOperationalNotes
        // soft-delete-bypass: load changed notes for hidden Orders so clients can remove stale cards.
        .IgnoreQueryFilters().AsNoTracking()
        .Where(note => note.KitchenBoardSequence > lower && note.KitchenBoardSequence <= upper);

    private static bool IsActiveWork(OrderOperationalNote note) =>
        note.Audience == OrderNoteAudience.Kitchen && note.KitchenChangesJson is not null
        && !note.WithdrawnAt.HasValue && !note.Order.IsDeleted
        && note.Order.ExternalReference is null;

    private static IQueryable<OrderOperationalNote> LoadNotes(
        IQueryable<OrderOperationalNote> query) => query
        .Include(note => note.Order).ThenInclude(order => order.RoutingStates);

    private static async Task<List<KitchenBoardCorrectionDto>> ProjectAsync(
        ApplicationDbContext context,
        List<OrderOperationalNote> notes,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (notes.Count == 0) return [];
        var ids = notes.Select(note => note.Id).ToArray();
        var completions = await context.KitchenBoardWorkCompletions.AsNoTracking()
            .Where(work => ids.Contains(work.WorkItemId)
                && work.Kind == KitchenBoardWorkKind.AmendmentCorrection)
            .ToDictionaryAsync(work => work.WorkItemId, cancellationToken);
        var printed = await context.DeviceOrderReceipts.AsNoTracking()
            .Where(receipt => receipt.JobId.HasValue && ids.Contains(receipt.JobId.Value)
                && receipt.JobType == DevicePrintJobType.Update
                && receipt.Revision == PrinterUpdateRevisions.Original
                && receipt.Status == DevicePrintStatus.Printed)
            .Select(receipt => new
            {
                JobId = receipt.JobId!.Value,
                receipt.OrderId,
                receipt.Target,
                receipt.DeviceId,
            })
            .ToListAsync(cancellationToken);
        var printedKeys = printed.Select(receipt => (
            receipt.OrderId, receipt.JobId, receipt.Target, receipt.DeviceId)).ToHashSet();

        return notes.Select(note =>
        {
            completions.TryGetValue(note.Id, out var completion);
            var target = note.KitchenTarget;
            var route = target.HasValue
                ? note.Order.RoutingStates.FirstOrDefault(state => state.IsRequired
                    && state.Target == target.Value)
                : null;
            var withdrawn = note.WithdrawnAt.HasValue;
            var printerCompleted = target.HasValue && route?.DeviceId is { } deviceId
                && printedKeys.Contains((note.OrderId, note.Id, target.Value, deviceId));
            var completed = withdrawn || completion is not null || printerCompleted;
            var changes = withdrawn ? [] : MapChanges(note.KitchenChangesJson);
            return new KitchenBoardCorrectionDto(
                note.Id,
                note.OrderId,
                note.Order.OrderNumber,
                note.Order.Status.ToString(),
                note.Order.Version,
                note.Order.TableId,
                note.Order.TableLabel,
                note.Order.TableNumber,
                note.Order.ServiceSessionId,
                note.AmendmentId,
                note.AccountRevision,
                target?.ToString(),
                withdrawn ? string.Empty : note.Text,
                withdrawn,
                completed,
                !completed && route?.Status == DevicePrintStatus.NotConfigured
                    && route.DeviceId is null,
                !target.HasValue ? "Unrouted" : route?.Status.ToString() ?? "Unrouted",
                note.CreatedAt,
                changes);
        }).ToList();
    }

    private static List<KitchenBoardChangeDto> MapChanges(string? json) =>
        KitchenChangeSnapshot.Deserialize(json).Select(change => new KitchenBoardChangeDto(
            change.Kind.ToString(),
            change.ReplacementDispatchedOrderId,
            change.ReplacementDispatchedOrderNumber,
            MapItem(change.Previous),
            MapItem(change.Current))).ToList();

    private static KitchenBoardItemDto? MapItem(OrderItemDto? item) => item is null ? null
        : new KitchenBoardItemDto(item.Id, item.ProductName, item.VariationName, item.Quantity,
            item.Kind?.ToString(), item.SpecialInstructions,
            (item.IngredientCustomizations ?? []).Select(value => new KitchenBoardIngredientDto(
                value.IngredientId, value.IngredientName, value.Quantity, value.IsRemoved, value.IsAddOn)).ToList(),
            (item.SideItems ?? []).Select(child => MapItem(child)!).ToList());

    private static string WatermarkCursor(
        IOperationalQueueCursor cursor, string filterHash, long upper, int pageSize, int totalCount) =>
        KitchenBoardCursorPolicy.Protect(cursor, filterHash, StreamName,
            OperationalQueueSyncModes.Watermark, upper, 0, null, null, 1, pageSize, totalCount);
}
