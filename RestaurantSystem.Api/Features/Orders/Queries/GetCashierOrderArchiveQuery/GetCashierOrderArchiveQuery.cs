using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using OrderFilters = RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery.GetOrdersQuery;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCashierOrderArchiveQuery;

public sealed record GetCashierOrderArchiveQuery(
    DateOnly? BusinessDate,
    string? Search,
    string? Status,
    string? PaymentStatus,
    string? OrderType,
    int Page = 1,
    int PageSize = 50,
    int PriorDayUnresolvedPage = 1,
    int PriorDayUnresolvedPageSize = 50)
    : IQuery<ApiResponse<CashierOrderArchiveDto>>;

public sealed class GetCashierOrderArchiveQueryHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITenantClock clock,
    IOrderQueueProjection projection,
    ILogger<GetCashierOrderArchiveQueryHandler> logger)
    : IQueryHandler<GetCashierOrderArchiveQuery, ApiResponse<CashierOrderArchiveDto>>
{
    private const int MaximumPageSize = 100;

    public async Task<ApiResponse<CashierOrderArchiveDto>> Handle(
        GetCashierOrderArchiveQuery request, CancellationToken cancellationToken)
    {
        Validate(request);
        var businessDate = request.BusinessDate ?? DateOnly.FromDateTime(clock.Now.Date);
        var ordersQuery = OperationalOrderQueryBuilder.Build(context, new OrderFilters(
            request.Status, request.PaymentStatus, request.OrderType, null, null, null,
            request.Search, null, TenantDay: businessDate, OrderBy: "OrderDate",
            Descending: true, Scope: OrderListScope.All), currentUser, clock, logger);
        var orders = await ReadPageAsync(ordersQuery, request.Page, request.PageSize, cancellationToken);

        // Keep the legacy response field for clients, but leave prior days in the dated archive.
        // Hiding old work does not settle, cancel, or otherwise rewrite the order or its payments.
        var unresolved = new PagedResult<OrderDto>(
            [], 0, 1, request.PriorDayUnresolvedPageSize, 0);

        return ApiResponse<CashierOrderArchiveDto>.SuccessWithData(
            new CashierOrderArchiveDto(businessDate, orders, unresolved));
    }

    private async Task<PagedResult<OrderDto>> ReadPageAsync(
        IQueryable<Order> query, int requestedPage, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var page = Math.Min(requestedPage, Math.Max(1, totalPages));
        var rows = await query.OrderByDescending(order => order.OrderDate).ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<OrderDto>(rows.Select(order =>
            projection.Project(order, includePermittedActions: true)).ToList(),
            totalCount, page, pageSize, totalPages);
    }

    private void Validate(GetCashierOrderArchiveQuery request)
    {
        if (!currentUser.IsAdmin && currentUser.Role != UserRole.Cashier)
            throw new ForbiddenException("The cashier archive is available only to admins and cashiers.");
        ValidatePage(request.Page, request.PageSize, "archive");
        ValidatePage(request.PriorDayUnresolvedPage, request.PriorDayUnresolvedPageSize, "unresolved queue");
        if (request.Search?.Length > 200)
            throw new BadRequestException("Archive search text cannot exceed 200 characters.");
        if (!IsEnumFilterValid<OrderStatus>(request.Status, allowMany: true)
            || !IsEnumFilterValid<PaymentStatus>(request.PaymentStatus, allowMany: false)
            || !IsEnumFilterValid<OrderType>(request.OrderType, allowMany: false))
            throw new BadRequestException("An archive filter value is invalid.");
    }

    private static void ValidatePage(int page, int pageSize, string label)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
            throw new BadRequestException($"Choose a positive {label} page and a page size between 1 and 100.");
    }

    private static bool IsEnumFilterValid<TEnum>(string? value, bool allowMany)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var values = allowMany ? value.Split(',', StringSplitOptions.RemoveEmptyEntries) : [value];
        return values.Length > 0 && values.All(token =>
            Enum.TryParse<TEnum>(token.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed));
    }
}
