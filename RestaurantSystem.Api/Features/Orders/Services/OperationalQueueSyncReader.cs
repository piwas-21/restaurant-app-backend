using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Reads snapshot and change pages behind the operational queue sync contract.</summary>
public sealed class OperationalQueueSyncReader : IOperationalQueueSyncReader
{
    private const string RemovalStatusExit = "status-exit";
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantClock _clock;
    private readonly IOrderMappingService _mapping;
    private readonly IOperationalQueueCursor _cursor;
    private readonly ILogger<OperationalQueueSyncReader> _logger;

    public OperationalQueueSyncReader(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITenantClock clock,
        IOrderMappingService mapping,
        IOperationalQueueCursor cursor,
        ILogger<OperationalQueueSyncReader> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _mapping = mapping;
        _cursor = cursor;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> ReadSnapshotAsync(
        GetOrdersQuery query, string filterHash, CancellationToken cancellationToken)
    {
        var upperSequence = await CurrentSequenceAsync(cancellationToken);
        var orders = OperationalOrderQueryBuilder.ApplySnapshotBoundary(
            OperationalOrderQueryBuilder.Build(_context, query, _currentUser, _clock, _logger),
            upperSequence);
        var totalCount = await orders.CountAsync(cancellationToken);
        var page = OperationalOrderQueryBuilder.ApplyOrdering(orders, query.OrderBy, query.Descending);
        var rows = await page
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        return BuildSnapshotResponse(
            query, filterHash, upperSequence, totalCount, rows, query.Page, query.PageSize);
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> ReadSnapshotNextAsync(
        GetOrdersQuery query,
        OperationalQueueCursorPayload payload,
        CancellationToken cancellationToken)
    {
        var orders = OperationalOrderQueryBuilder.ApplySnapshotBoundary(
            OperationalOrderQueryBuilder.Build(_context, query, _currentUser, _clock, _logger),
            payload.UpperSequence);
        orders = OperationalOrderQueryBuilder.ApplyPosition(
            orders, query.OrderBy ?? "OrderDate", query.Descending, payload.Position, payload.PositionId);
        var page = OperationalOrderQueryBuilder.ApplyOrdering(orders, query.OrderBy, query.Descending);
        var rows = await page.Take(payload.PageSize + 1).ToListAsync(cancellationToken);

        return BuildSnapshotResponse(
            query, payload.FilterHash, payload.UpperSequence, payload.TotalCount, rows,
            payload.Page + 1, payload.PageSize);
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> ReadChangesAsync(
        GetOrdersQuery query,
        OperationalQueueCursorPayload payload,
        CancellationToken cancellationToken)
    {
        var firstPage = payload.Mode == OperationalQueueSyncModes.Watermark;
        var lowerSequence = firstPage ? payload.UpperSequence : payload.LowerSequence;
        var upperSequence = firstPage
            ? await CurrentSequenceAsync(cancellationToken)
            : payload.UpperSequence;
        var pageSize = payload.PageSize;
        var totalCount = firstPage
            ? await CountChangesAsync(lowerSequence, upperSequence, cancellationToken)
            : payload.TotalCount;
        var changes = _context.OrderChanges
            .AsNoTracking()
            .Where(change => change.Sequence > lowerSequence && change.Sequence <= upperSequence);

        if (!firstPage)
        {
            if (!long.TryParse(payload.Position, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            {
                throw new BadRequestException(
                    "The operational queue synchronization cursor is invalid.",
                    ErrorCodes.InvalidOperationalQueueCursor);
            }

            changes = changes.Where(change => change.Sequence > position);
        }

        var rows = await changes
            .OrderBy(change => change.Sequence)
            .Take(pageSize + 1)
            .Select(change => new ChangeRow(change.Sequence, change.OrderId, change.Kind, change.Reason))
            .ToListAsync(cancellationToken);

        var page = rows.Take(pageSize).ToList();
        var ids = page.Select(change => change.OrderId).Distinct().ToList();
        var matchingOrders = ids.Count == 0
            ? []
            : await OperationalOrderQueryBuilder.Build(
                    _context, query, _currentUser, _clock, _logger)
                .Where(order => ids.Contains(order.Id))
                .ToListAsync(cancellationToken);
        var byId = matchingOrders.ToDictionary(order => order.Id);
        var items = new List<OrderDto>();
        var removals = new List<PageSyncRemoval>();

        foreach (var change in page)
        {
            if (change.Kind == OrderChangeKind.Remove || !byId.TryGetValue(change.OrderId, out var order))
            {
                removals.Add(new PageSyncRemoval(
                    change.OrderId, change.Reason ?? RemovalStatusExit));
                continue;
            }

            // An item in Changes mode is an Upsert. The DTO is read from the current matching row;
            // the journal sequence is the ordering marker, so a client can apply pages in order and
            // ignore an older response that arrives after a newer one.
            items.Add(await _mapping.MapToOrderDtoAsync(order, cancellationToken));
        }

        var hasMore = rows.Count > pageSize;
        var nextCursor = hasMore
            ? _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Changes,
                FilterHash = payload.FilterHash,
                UpperSequence = upperSequence,
                LowerSequence = lowerSequence,
                Position = rows[pageSize - 1].Sequence.ToString(CultureInfo.InvariantCulture),
                Page = payload.Page + 1,
                PageSize = pageSize,
                TotalCount = totalCount,
            })
            : null;
        var watermark = hasMore
            ? null
            : _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Watermark,
                FilterHash = payload.FilterHash,
                UpperSequence = upperSequence,
                Page = 1,
                PageSize = pageSize,
                TotalCount = totalCount,
            });
        var result = new PagedResult<OrderDto>(
            items,
            totalCount,
            firstPage ? 1 : payload.Page + 1,
            pageSize,
            TotalPages(totalCount, pageSize))
        {
            Sync = new PageSyncMetadata(
                OperationalQueueSyncModes.Changes,
                nextCursor,
                watermark,
                hasMore,
                removals),
        };

        return ApiResponse<PagedResult<OrderDto>>.SuccessWithData(result);
    }

    private PagedResult<OrderDto> BuildSnapshotResult(
        GetOrdersQuery query,
        string filterHash,
        long upperSequence,
        int totalCount,
        List<Order> rows,
        int pageNumber,
        int pageSize)
    {
        var hasMore = rows.Count > pageSize;
        var pageRows = rows.Take(pageSize).ToList();
        var nextCursor = hasMore && pageRows.Count > 0
            ? _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Snapshot,
                FilterHash = filterHash,
                UpperSequence = upperSequence,
                Position = OperationalOrderQueryBuilder.SortValue(pageRows[^1], query.OrderBy),
                PositionId = pageRows[^1].Id,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
            })
            : null;
        var watermark = hasMore
            ? null
            : _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Watermark,
                FilterHash = filterHash,
                UpperSequence = upperSequence,
                Page = 1,
                PageSize = pageSize,
                TotalCount = totalCount,
            });

        return new PagedResult<OrderDto>(
            pageRows.Select(_mapping.MapToOrderDto).ToList(),
            totalCount,
            pageNumber,
            pageSize,
            TotalPages(totalCount, pageSize))
        {
            Sync = new PageSyncMetadata(
                OperationalQueueSyncModes.Snapshot,
                nextCursor,
                watermark,
                hasMore,
                []),
        };
    }

    private ApiResponse<PagedResult<OrderDto>> BuildSnapshotResponse(
        GetOrdersQuery query,
        string filterHash,
        long upperSequence,
        int totalCount,
        List<Order> rows,
        int pageNumber,
        int pageSize) =>
        ApiResponse<PagedResult<OrderDto>>.SuccessWithData(
            BuildSnapshotResult(query, filterHash, upperSequence, totalCount, rows, pageNumber, pageSize));

    private async Task<long> CurrentSequenceAsync(CancellationToken cancellationToken) =>
        await _context.OrderChanges
            .Select(change => (long?)change.Sequence)
            .MaxAsync(cancellationToken) ?? 0L;

    private Task<int> CountChangesAsync(
        long lowerSequence, long upperSequence, CancellationToken cancellationToken) =>
        _context.OrderChanges
            .CountAsync(
                change => change.Sequence > lowerSequence && change.Sequence <= upperSequence,
                cancellationToken);

    private static int TotalPages(int totalCount, int pageSize) =>
        (int)Math.Ceiling(totalCount / (double)pageSize);

    private sealed record ChangeRow(
        long Sequence, Guid OrderId, OrderChangeKind Kind, string? Reason);
}
