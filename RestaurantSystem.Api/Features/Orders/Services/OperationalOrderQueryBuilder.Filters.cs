using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static partial class OperationalOrderQueryBuilder
{
    private static IQueryable<Order> CreateBaseQuery(ApplicationDbContext context) =>
        context.Orders
            .IncludeOrderLineGraph()
            .Include(order => order.Payments)
            .Include(order => order.StatusHistory)
            .Include(order => order.DeliveryAddress)
            .Where(order => !order.IsDeleted)
            .AsNoTracking()
            .AsSplitQuery()
            .AsQueryable();

    private static IQueryable<Order> ApplyCallerFilter(
        IQueryable<Order> orders, ICurrentUserService currentUser)
    {
        if (currentUser.IsStaff)
        {
            return orders;
        }

        return currentUser.UserId.HasValue
            ? orders.Where(order => order.UserId == currentUser.UserId.Value)
            : orders.Where(_ => false);
    }

    private static IQueryable<Order> ApplyScopeFilter(
        IQueryable<Order> orders, GetOrdersQuery query) =>
        query.Scope == OrderListScope.Operational
            ? orders.Where(OrderSettlementEligibility.OperationalQueuePredicate())
            : orders;

    private static IQueryable<Order> ApplyRequestedUserFilter(
        IQueryable<Order> orders, GetOrdersQuery query) =>
        query.UserId.HasValue
            ? orders.Where(order => order.UserId == query.UserId.Value)
            : orders;

    private static IQueryable<Order> ApplyTableFilter(
        IQueryable<Order> orders, GetOrdersQuery query) =>
        query.TableNumber.HasValue
            ? orders.Where(order => order.TableNumber == query.TableNumber.Value)
            : orders;

    private static IQueryable<Order> ApplyFocusFilter(
        IQueryable<Order> orders, GetOrdersQuery query)
    {
        if (!query.IsFocusOrder.HasValue)
        {
            return orders;
        }

        return query.IsFocusOrder.Value
            ? orders.Where(order => order.Focus != null)
            : orders.Where(order => order.Focus == null);
    }

    private static IQueryable<Order> ApplyModifiedSinceFilter(
        IQueryable<Order> orders, GetOrdersQuery query)
    {
        var modifiedSince = QueryInstant.AsUtc(query.ModifiedSince);
        return modifiedSince.HasValue
            ? orders.Where(order =>
                order.CreatedAt > modifiedSince.Value
                || (order.UpdatedAt.HasValue && order.UpdatedAt.Value > modifiedSince.Value))
            : orders;
    }

    private static IQueryable<Order> ApplySearchFilter(
        IQueryable<Order> orders, GetOrdersQuery query)
    {
        if (string.IsNullOrEmpty(query.Search))
        {
            return orders;
        }

        var search = query.Search.ToLower();
        return orders.Where(order =>
            order.OrderNumber.ToLower().Contains(search)
            || (order.CustomerName != null && order.CustomerName.ToLower().Contains(search))
            || (order.CustomerEmail != null && order.CustomerEmail.ToLower().Contains(search))
            || (order.CustomerPhone != null && order.CustomerPhone.ToLower().Contains(search))
            || (order.TableNumber.HasValue && order.TableNumber.Value.ToString().Contains(search)));
    }
}
