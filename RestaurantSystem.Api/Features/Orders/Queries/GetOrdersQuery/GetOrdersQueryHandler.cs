using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

public sealed class GetOrdersQueryHandler
    : IQueryHandler<GetOrdersQuery, ApiResponse<PagedResult<OrderDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantClock _clock;
    private readonly IOrderMappingService _mapping;
    private readonly IOperationalQueueCursor _cursor;
    private readonly IOperationalQueueSyncReader _sync;
    private readonly ILogger<GetOrdersQueryHandler> _logger;

    public GetOrdersQueryHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITenantClock clock,
        IOrderMappingService mapping,
        IOperationalQueueCursor cursor,
        IOperationalQueueSyncReader sync,
        ILogger<GetOrdersQueryHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _mapping = mapping;
        _cursor = cursor;
        _sync = sync;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> Handle(
        GetOrdersQuery query, CancellationToken cancellationToken)
    {
        if (query.Scope != OrderListScope.Operational)
        {
            if (query.SyncCursor is not null)
            {
                throw InvalidCursor();
            }

            return await ReadLegacyPageAsync(query, cancellationToken);
        }

        var filterHash = OperationalOrderQueryBuilder.FilterHash(query, _currentUser);
        if (query.SyncCursor is null)
        {
            // Page 1 starts the cursor protocol. Existing offset clients may still request later
            // pages, but those responses deliberately carry no sync watermark because earlier
            // snapshot rows were omitted.
            if (query.Page > 1)
            {
                return await ReadLegacyPageAsync(query, cancellationToken);
            }

            return await _sync.ReadSnapshotAsync(query, filterHash, cancellationToken);
        }

        var payload = _cursor.Read(query.SyncCursor);
        if (!string.Equals(payload.FilterHash, filterHash, StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "The operational queue synchronization cursor does not match these filters.",
                ErrorCodes.InvalidOperationalQueueCursor);
        }

        return payload.Mode switch
        {
            OperationalQueueSyncModes.Snapshot =>
                await _sync.ReadSnapshotNextAsync(query, payload, cancellationToken),
            OperationalQueueSyncModes.Watermark =>
                await _sync.ReadChangesAsync(query, payload, cancellationToken),
            OperationalQueueSyncModes.Changes =>
                await _sync.ReadChangesAsync(query, payload, cancellationToken),
            _ => throw new BadRequestException(
                "The operational queue synchronization cursor is invalid.",
                ErrorCodes.InvalidOperationalQueueCursor),
        };
    }

    private async Task<ApiResponse<PagedResult<OrderDto>>> ReadLegacyPageAsync(
        GetOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = OperationalOrderQueryBuilder.Build(
            _context, query, _currentUser, _clock, _logger);
        var totalCount = await orders.CountAsync(cancellationToken);
        var page = OperationalOrderQueryBuilder.ApplyOrdering(orders, query.OrderBy, query.Descending);
        var rows = await page
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(_mapping.MapToOrderDto).ToList();
        var result = new PagedResult<OrderDto>(
            items, totalCount, query.Page, query.PageSize,
            (int)Math.Ceiling(totalCount / (double)query.PageSize));

        _logger.LogInformation("Retrieved {Count} orders out of {TotalCount} total", items.Count, totalCount);
        return ApiResponse<PagedResult<OrderDto>>.SuccessWithData(result);
    }

    private static BadRequestException InvalidCursor() => new(
        "The operational queue synchronization cursor is invalid.",
        ErrorCodes.InvalidOperationalQueueCursor);
}
