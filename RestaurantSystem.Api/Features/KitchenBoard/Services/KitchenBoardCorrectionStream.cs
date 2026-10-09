using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
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
        KitchenBoardStreamReadContext request)
    {
        var context = request.Context;
        var cancellationToken = request.CancellationToken;
        if (string.IsNullOrWhiteSpace(request.CursorValue))
        {
            var total = await KitchenNotes(context, request.CurrentWatermark).CountAsync(cancellationToken);
            return await SnapshotAsync(request, request.CurrentWatermark, total, null);
        }

        var payload = KitchenBoardCursorPolicy.Read(
            request.Cursor, request.CursorValue, request.FilterHash, StreamName, request.PageSize);
        if (payload.Mode == OperationalQueueSyncModes.Snapshot)
        {
            return await SnapshotAsync(
                request, payload.UpperSequence, payload.TotalCount, payload.PositionId, payload.Page);
        }

        return await ChangesAsync(request, payload);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> SnapshotAsync(
        KitchenBoardStreamReadContext request,
        long upper,
        int totalCount,
        Guid? afterId,
        int page = 1)
    {
        var context = request.Context;
        var pageSize = request.PageSize;
        var cancellationToken = request.CancellationToken;
        var query = KitchenNotes(context, upper);
        if (afterId.HasValue)
        {
            query = query.Where(note => note.Id.CompareTo(afterId.Value) > 0);
        }

        var rows = await query.OrderBy(note => note.Id)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var items = await ProjectAsync(request, rows.Take(pageSize).ToList());
        var hasMore = rows.Count > pageSize;
        var next = hasMore && items.Count > 0
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Snapshot,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    Position = items[^1].WorkItemId.ToString("D"),
                    PositionId = items[^1].WorkItemId,
                    Page = page + 1,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                })
            : WatermarkCursor(request, upper, totalCount);
        return new KitchenBoardPageDto<KitchenBoardCorrectionDto>(
            items, totalCount, hasMore, [], next, upper, OperationalQueueSyncModes.Snapshot);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> ChangesAsync(
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

        var notes = ChangedKitchenNotes(context, lower, upper);
        if (payload.Mode == OperationalQueueSyncModes.Changes)
        {
            var afterSequence = KitchenBoardCursorPolicy.ParsePosition(payload);
            notes = notes.Where(note => note.KitchenBoardSequence > afterSequence);
        }

        // Read note scalars without joining Order: EF's required-navigation query filter
        // would hide changed notes whose parent was soft-deleted, losing removals.
        var rows = await notes.OrderBy(note => note.KitchenBoardSequence)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var pageRows = rows.Take(pageSize).ToList();
        var projected = await ProjectAsync(request, pageRows);
        var projectedById = projected.ToDictionary(item => item.WorkItemId);
        var rowIds = pageRows.Select(note => note.Id).ToArray();
        var activeIds = await KitchenBoardWorkRules.ActiveCorrections(context)
            .Where(note => rowIds.Contains(note.Id)).Select(note => note.Id)
            .ToHashSetAsync(cancellationToken);
        var items = projected.Where(item => activeIds.Contains(item.WorkItemId)
            && !item.IsCompleted && !item.Withdrawn).ToList();
        var tombstones = projected.Where(item => item.Withdrawn).ToList();
        var removed = pageRows.Where(note => !projectedById.TryGetValue(note.Id, out var item)
                || !activeIds.Contains(note.Id) || item.IsCompleted || item.Withdrawn)
            .Select(note => note.Id).Distinct().ToList();
        items.AddRange(tombstones);
        var count = await KitchenBoardWorkRules.ActiveCorrections(context).CountAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var next = hasMore
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Changes,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    LowerSequence = lower,
                    Position = rows[pageSize - 1].KitchenBoardSequence.ToString(CultureInfo.InvariantCulture),
                    Page = payload.Page + 1,
                    PageSize = pageSize,
                    TotalCount = count,
                })
            : WatermarkCursor(request, upper, count);
        return new KitchenBoardPageDto<KitchenBoardCorrectionDto>(
            items, count, hasMore, removed, next, upper,
            hasMore ? OperationalQueueSyncModes.Changes : OperationalQueueSyncModes.Watermark);
    }

    private static IQueryable<OrderOperationalNote> KitchenNotes(
        ApplicationDbContext context, long? upper) => KitchenBoardWorkRules.ActiveCorrections(context)
        .Where(note => !upper.HasValue || note.KitchenBoardSequence <= upper.Value);

    private static IQueryable<OrderOperationalNote> ChangedKitchenNotes(
        ApplicationDbContext context, long lower, long upper) => context.OrderOperationalNotes
        .AsNoTracking()
        .Where(note => note.Audience == OrderNoteAudience.Kitchen
            && note.KitchenBoardSequence > lower && note.KitchenBoardSequence <= upper);

    private static async Task<List<KitchenBoardCorrectionDto>> ProjectAsync(
        KitchenBoardStreamReadContext request,
        List<OrderOperationalNote> notes)
    {
        var context = request.Context;
        var cancellationToken = request.CancellationToken;
        if (notes.Count == 0) return [];
        var orderIds = notes.Select(note => note.OrderId).Distinct().ToArray();
        var orders = await context.Orders.AsNoTracking()
            .Where(order => orderIds.Contains(order.Id) && order.ExternalReference == null)
            .Include(order => order.RoutingStates)
            .ToDictionaryAsync(order => order.Id, cancellationToken);
        var visibleNotes = notes.Where(note => orders.ContainsKey(note.OrderId)).ToList();
        if (visibleNotes.Count == 0) return [];
        var ids = visibleNotes.Select(note => note.Id).ToArray();
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

        return visibleNotes.Select(note => ProjectNote(note, orders, completions, printedKeys)).ToList();
    }

    private static KitchenBoardCorrectionDto ProjectNote(
        OrderOperationalNote note,
        Dictionary<Guid, Order> orders,
        Dictionary<Guid, KitchenBoardWorkCompletion> completions,
        HashSet<(Guid OrderId, Guid JobId, DevicePrintTarget Target, string DeviceId)> printedKeys)
    {
        var order = orders[note.OrderId];
        completions.TryGetValue(note.Id, out var completion);
        var target = note.KitchenTarget;
        var route = target.HasValue
            ? order.RoutingStates.FirstOrDefault(state => state.IsRequired
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
            order.OrderNumber,
            order.Status.ToString(),
            order.Version,
            order.TableId,
            order.TableLabel,
            order.TableNumber,
            order.ServiceSessionId,
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
