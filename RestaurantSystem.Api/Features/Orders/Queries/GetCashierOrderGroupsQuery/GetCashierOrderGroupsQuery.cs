using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using OrderFilters = RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery.GetOrdersQuery;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCashierOrderGroupsQuery;

public sealed record GetCashierOrderGroupsQuery(OrderFilters Filters)
    : IQuery<ApiResponse<PagedResult<CashierOrderGroupDto>>>;

public sealed class GetCashierOrderGroupsQueryHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITenantClock clock,
    IOrderQueueProjection projection,
    ILogger<GetCashierOrderGroupsQueryHandler> logger)
    : IQueryHandler<GetCashierOrderGroupsQuery, ApiResponse<PagedResult<CashierOrderGroupDto>>>
{
    private const int MaximumPageSize = 100;

    public async Task<ApiResponse<PagedResult<CashierOrderGroupDto>>> Handle(
        GetCashierOrderGroupsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsStaff)
            throw new ForbiddenException("The cashier queue is available only to staff.");
        var filters = request.Filters;
        if (filters.Page < 1 || filters.PageSize is < 1 or > MaximumPageSize)
            throw new BadRequestException("Choose a positive page and a page size between 1 and 100.");
        if (filters.SyncCursor is not null || filters.ModifiedSince.HasValue)
            throw new BadRequestException("The grouped cashier queue requires a complete page refresh.");
        if (!string.Equals(filters.OrderBy, "OrderDate", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("The grouped cashier queue is ordered by the latest order date.");

        await using var snapshot = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var matching = OperationalOrderQueryBuilder.Build(context, filters, currentUser, clock, logger);
        var groups = matching.Select(order => new
        {
            Id = order.ServiceSessionId.HasValue && order.ServiceSession!.Status == TableServiceSessionStatus.Open
                ? order.ServiceSessionId.Value : order.Id,
            SessionId = order.ServiceSessionId.HasValue && order.ServiceSession!.Status == TableServiceSessionStatus.Open
                ? order.ServiceSessionId : null,
            order.OrderDate,
        }).GroupBy(row => new { row.Id, row.SessionId })
            .Select(group => new { group.Key.Id, group.Key.SessionId, LatestDate = group.Max(row => row.OrderDate) });
        var total = await groups.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(total / (double)filters.PageSize);
        var pageNumber = Math.Min(filters.Page, Math.Max(1, totalPages));
        var ordered = filters.Descending
            ? groups.OrderByDescending(group => group.LatestDate).ThenBy(group => group.Id)
            : groups.OrderBy(group => group.LatestDate).ThenBy(group => group.Id);
        var page = await ordered.Skip((pageNumber - 1) * filters.PageSize)
            .Take(filters.PageSize).ToListAsync(cancellationToken);
        var sessionIds = page.Where(group => group.SessionId.HasValue)
            .Select(group => group.SessionId!.Value).ToArray();
        var standaloneIds = page.Where(group => !group.SessionId.HasValue).Select(group => group.Id).ToArray();

        // Filters choose groups; loading their full membership avoids losing an earlier round
        // merely because another round matched the search/status or appeared on a later page.
        var rows = await OperationalOrderQueryBuilder.Build(context,
                filters with
                {
                    Scope = OrderListScope.All,
                    Status = null,
                    PaymentStatus = null,
                    OrderType = null,
                    StartDate = null,
                    EndDate = null,
                    UserId = null,
                    Search = null,
                    IsFocusOrder = null,
                    TenantDay = null,
                    TenantStartDay = null,
                    TenantEndDay = null,
                    TableNumber = null,
                    MarketplaceOnly = false
                }, currentUser, clock, logger)
            .Where(order => standaloneIds.Contains(order.Id)
                || (order.ServiceSessionId.HasValue && sessionIds.Contains(order.ServiceSessionId.Value)))
            .OrderBy(order => order.OrderDate).ThenBy(order => order.Id).ToListAsync(cancellationToken);
        var bySession = rows.Where(order => order.ServiceSessionId.HasValue)
            .ToLookup(order => order.ServiceSessionId!.Value);
        var byId = rows.ToDictionary(order => order.Id);
        var releasedVisits = await context.TableServiceSessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, session => session.ReleasedAt, cancellationToken);
        var archivedOrderIds = (await context.TableOccupancyRecoveryDispositions.AsNoTracking()
            .Where(disposition => standaloneIds.Contains(disposition.OrderId) && disposition.WasLegacyUnassigned)
            .Select(disposition => disposition.OrderId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var items = page.Select(group =>
        {
            var members = group.SessionId.HasValue
                ? bySession[group.SessionId.Value].ToList()
                : new List<Order> { byId[group.Id] };
            return new CashierOrderGroupDto(
                $"{(group.SessionId.HasValue ? "visit" : "order")}:{group.Id:D}", group.SessionId,
                members[0].TableNumber,
                group.SessionId.HasValue ? releasedVisits[group.SessionId.Value] : null,
                !group.SessionId.HasValue && archivedOrderIds.Contains(group.Id),
                members.Select(order => projection.Project(order, includePermittedActions: true)).ToList());
        }).ToList();
        await snapshot.CommitAsync(cancellationToken);
        return ApiResponse<PagedResult<CashierOrderGroupDto>>.SuccessWithData(
            new PagedResult<CashierOrderGroupDto>(items, total, pageNumber, filters.PageSize, totalPages));
    }
}
