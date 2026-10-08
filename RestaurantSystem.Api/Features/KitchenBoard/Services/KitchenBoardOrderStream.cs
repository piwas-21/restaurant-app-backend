using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardOrderStream
{
    private const string StreamName = "orders";

    internal static async Task<KitchenBoardPageDto<KitchenBoardOrderDto>> ReadAsync(
        KitchenBoardStreamReadContext request)
    {
        var context = request.Context;
        var cancellationToken = request.CancellationToken;
        if (string.IsNullOrWhiteSpace(request.CursorValue))
        {
            var total = await EligibleOrders(context, request.CurrentWatermark).CountAsync(cancellationToken);
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

    private static async Task<KitchenBoardPageDto<KitchenBoardOrderDto>> SnapshotAsync(
        KitchenBoardStreamReadContext request,
        long upper,
        int totalCount,
        Guid? afterId,
        int page = 1)
    {
        var context = request.Context;
        var pageSize = request.PageSize;
        var cancellationToken = request.CancellationToken;
        var query = EligibleOrders(context, upper);
        if (afterId.HasValue)
        {
            query = query.Where(order => order.Id.CompareTo(afterId.Value) > 0);
        }

        var rows = await LoadOrderGraph(query)
            .OrderBy(order => order.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var items = await ProjectAsync(request, rows.Take(pageSize).ToList());
        var hasMore = rows.Count > pageSize;
        var next = hasMore && items.Count > 0
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Snapshot,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    Position = items[^1].OrderId.ToString("D"),
                    PositionId = items[^1].OrderId,
                    Page = page + 1,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                })
            : WatermarkCursor(request, upper, totalCount);

        return new KitchenBoardPageDto<KitchenBoardOrderDto>(
            items, totalCount, hasMore, [], next, upper, OperationalQueueSyncModes.Snapshot);
    }

    private static async Task<KitchenBoardPageDto<KitchenBoardOrderDto>> ChangesAsync(
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
        if (upper < lower)
        {
            throw KitchenBoardCursorPolicy.InvalidCursor();
        }

        var changes = context.OrderChanges.AsNoTracking()
            .Where(change => change.Sequence > lower && change.Sequence <= upper);
        if (payload.Mode == OperationalQueueSyncModes.Changes)
        {
            var afterSequence = KitchenBoardCursorPolicy.ParsePosition(payload);
            changes = changes.Where(change => change.Sequence > afterSequence);
        }

        var rows = await changes.OrderBy(change => change.Sequence)
            .Take(pageSize + 1).ToListAsync(cancellationToken);
        var pageRows = rows.Take(pageSize).ToList();
        var changedIds = pageRows.Select(change => change.OrderId).Distinct().ToArray();
        var current = changedIds.Length == 0
            ? []
            : await LoadOrderGraph(EligibleOrders(context, null)
                    .Where(order => changedIds.Contains(order.Id)))
                .ToListAsync(cancellationToken);
        var currentById = await ProjectAsync(request, current);
        var currentByIdMap = currentById.ToDictionary(order => order.OrderId);
        var removed = changedIds.Where(id => !currentByIdMap.ContainsKey(id)).ToList();
        var page = changedIds.Where(currentByIdMap.ContainsKey)
            .Select(id => currentByIdMap[id]).ToList();
        var count = await EligibleOrders(context, null).CountAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var next = hasMore
            ? KitchenBoardCursorPolicy.Protect(request.Cursor, request.FilterHash, StreamName,
                new OperationalQueueCursorRequest
                {
                    Mode = OperationalQueueSyncModes.Changes,
                    FilterHash = request.FilterHash,
                    UpperSequence = upper,
                    LowerSequence = lower,
                    Position = rows[pageSize - 1].Sequence.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    Page = payload.Page + 1,
                    PageSize = pageSize,
                    TotalCount = count,
                })
            : WatermarkCursor(request, upper, count);

        return new KitchenBoardPageDto<KitchenBoardOrderDto>(
            page, count, hasMore, removed, next, upper,
            hasMore ? OperationalQueueSyncModes.Changes : OperationalQueueSyncModes.Watermark);
    }

    private static IQueryable<Order> EligibleOrders(ApplicationDbContext context, long? upper) =>
        context.Orders.AsNoTracking()
            .Where(order => !order.IsDeleted && order.IsKitchenReleased
                && order.ExternalReference == null
                && (order.Status == OrderStatus.Confirmed
                    || order.Status == OrderStatus.Preparing
                    || order.Status == OrderStatus.Ready)
                && (!upper.HasValue || order.LastChangeSequence <= upper.Value));

    private static IQueryable<Order> LoadOrderGraph(
        IQueryable<Order> query) => query
        .Include(order => order.RoutingStates)
        .Include(order => order.Items).ThenInclude(item => item.IngredientSnapshots)
        .Include(order => order.Items).ThenInclude(item => item.Product!.DetailedIngredients)
        .Include(order => order.Items).ThenInclude(item => item.Menu!.MenuItems)
            .ThenInclude(menuItem => menuItem.Product.DetailedIngredients)
        .AsSplitQuery();

    private static async Task<List<KitchenBoardOrderDto>> ProjectAsync(
        KitchenBoardStreamReadContext request,
        List<Order> orders)
    {
        var context = request.Context;
        var cancellationToken = request.CancellationToken;
        if (orders.Count == 0) return [];
        var ids = orders.Select(order => order.Id).ToArray();
        var completions = await context.KitchenBoardWorkCompletions.AsNoTracking()
            .Where(work => ids.Contains(work.OrderId)
                && work.Kind == KitchenBoardWorkKind.InitialOrder
                && work.WorkItemId == work.OrderId)
            .ToDictionaryAsync(work => work.OrderId, cancellationToken);

        return orders.Select(order =>
        {
            completions.TryGetValue(order.Id, out var completed);
            var routes = order.RoutingStates.Where(state => state.IsRequired
                    && state.Target != DevicePrintTarget.Cashier)
                .OrderBy(state => state.Target)
                .Select(state => new KitchenBoardRouteDto(state.Target.ToString(), state.Status.ToString()))
                .ToList();
            var canComplete = completed is null && order.Status == OrderStatus.Ready
                && KitchenBoardWorkRules.HasOnlyNotConfiguredRoutes(order);
            return new KitchenBoardOrderDto(
                order.Id,
                order.OrderNumber,
                order.Type.ToString(),
                order.Status.ToString(),
                order.TableId,
                order.TableLabel,
                order.TableNumber,
                order.ServiceSessionId,
                order.CreatedAt,
                order.Version,
                completed is not null,
                completed?.CreatedAt,
                canComplete,
                routes,
                MapItems(order.Items, request.Logger));
        }).ToList();
    }

    private static List<KitchenBoardItemDto> MapItems(
        IEnumerable<OrderItem> source, ILogger? logger)
    {
        var rows = source.ToList();
        var children = rows.ToLookup(item => item.ParentOrderItemId);
        return children[null].Select(item => MapItem(item, null, children, logger)).ToList();
    }

    private static KitchenBoardItemDto MapItem(
        OrderItem item,
        OrderItem? parent,
        ILookup<Guid?, OrderItem> children,
        ILogger? logger)
    {
        var ingredients = logger is null
            ? item.IngredientSnapshots
                .Where(row => row.IsRemoved || (row.Quantity > 0 && (row.Quantity > 1 || row.IsAddOn)))
                .OrderBy(row => row.SortOrder)
                .Select(row => new KitchenBoardIngredientDto(row.IngredientId,
                    row.IngredientName, row.Quantity, row.IsRemoved, row.IsAddOn)).ToList()
            : OrderIngredientCustomizations.Map(item, logger)?.Select(row =>
                new KitchenBoardIngredientDto(row.IngredientId, row.IngredientName,
                    row.Quantity, row.IsRemoved, row.IsAddOn)).ToList() ?? [];
        var childItems = children[item.Id].OrderBy(child => child.CreatedAt).ThenBy(child => child.Id)
            .Select(child => MapItem(child, item, children, logger)).ToList();
        var kind = parent is null ? null : OrderChildRendering.DisplayKind(item, parent).ToString();
        var quantity = parent is null ? item.Quantity : OrderChildRendering.LineQuantity(item, parent);
        return new KitchenBoardItemDto(item.Id, item.ProductName, item.VariationName,
            quantity, kind, item.SpecialInstructions, ingredients, childItems);
    }

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
