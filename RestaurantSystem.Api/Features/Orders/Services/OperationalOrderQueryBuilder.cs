using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Builds the shared order filters and keyset predicates for the orders read.</summary>
internal static partial class OperationalOrderQueryBuilder
{
    public static IQueryable<Order> Build(
        ApplicationDbContext context,
        GetOrdersQuery query,
        ICurrentUserService currentUser,
        ITenantClock clock,
        ILogger logger)
    {
        var orders = CreateBaseQuery(context);
        orders = ApplyCallerFilter(orders, currentUser);
        orders = ApplyScopeFilter(orders, query);
        orders = ApplyEnumFilters(orders, query);
        orders = ApplyDateFilters(orders, query, clock, logger);
        orders = ApplyRequestedUserFilter(orders, query);
        orders = ApplyTableFilter(orders, query);
        orders = ApplyFocusFilter(orders, query);
        orders = ApplyModifiedSinceFilter(orders, query);
        return ApplySearchFilter(orders, query);
    }

    public static IQueryable<Order> ApplySnapshotBoundary(
        IQueryable<Order> orders, long upperSequence) =>
        orders.Where(order => order.LastChangeSequence <= upperSequence);

    private static IQueryable<Order> ApplyEnumFilters(IQueryable<Order> orders, GetOrdersQuery query)
    {
        if (!string.IsNullOrEmpty(query.Status))
        {
            var statuses = query.Status.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => Enum.TryParse<OrderStatus>(token.Trim(), out var value) ? (OrderStatus?)value : null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
            if (statuses.Count > 0)
            {
                orders = orders.Where(order => statuses.Contains(order.Status));
            }
        }

        if (!string.IsNullOrEmpty(query.PaymentStatus)
            && Enum.TryParse<PaymentStatus>(query.PaymentStatus, out var paymentStatus))
        {
            orders = orders.Where(order => order.PaymentStatus == paymentStatus);
        }

        if (!string.IsNullOrEmpty(query.OrderType)
            && Enum.TryParse<OrderType>(query.OrderType, out var orderType))
        {
            orders = orders.Where(order => order.Type == orderType);
        }

        return orders;
    }

    private static IQueryable<Order> ApplyDateFilters(
        IQueryable<Order> orders,
        GetOrdersQuery query,
        ITenantClock clock,
        ILogger logger)
    {
        if (query.Scope == OrderListScope.Operational)
        {
            logger.LogInformation("Filtered orders by operational queue scope; date bounds ignored");
            return orders;
        }

        if (query.TenantDay.HasValue)
        {
            var (startUtc, endUtc) = clock.TenantDayWindowUtc(query.TenantDay.Value);
            logger.LogInformation(
                "Filtered orders by tenant day {TenantDay} ({ZoneId}, [{StartUtc:o}, {EndUtc:o}))",
                query.TenantDay.Value, clock.TimeZone.Id, startUtc, endUtc);
            return orders.Where(order => order.OrderDate >= startUtc && order.OrderDate < endUtc);
        }

        if (query.TenantStartDay.HasValue || query.TenantEndDay.HasValue)
        {
            if (!query.TenantStartDay.HasValue || !query.TenantEndDay.HasValue
                || query.TenantStartDay.Value > query.TenantEndDay.Value)
            {
                throw new BadRequestException("A valid tenant day range requires an ordered start and end day.");
            }
            if (query.TenantDay.HasValue)
            {
                throw new BadRequestException("Use either tenantDay or a tenant day range, not both.");
            }

            var (startUtc, _) = clock.TenantDayWindowUtc(query.TenantStartDay.Value);
            var (_, endUtc) = clock.TenantDayWindowUtc(query.TenantEndDay.Value);
            logger.LogInformation(
                "Filtered orders by tenant day range {StartDay}..{EndDay} ({ZoneId}, [{StartUtc:o}, {EndUtc:o}))",
                query.TenantStartDay.Value, query.TenantEndDay.Value,
                clock.TimeZone.Id, startUtc, endUtc);
            return orders.Where(order => order.OrderDate >= startUtc && order.OrderDate < endUtc);
        }

        var startDate = QueryInstant.AsUtc(query.StartDate);
        var endDate = QueryInstant.AsUtc(query.EndDate);
        if (startDate.HasValue)
        {
            orders = orders.Where(order => order.OrderDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            orders = orders.Where(order => order.OrderDate <= endDate.Value);
        }

        return orders;
    }

    private static IQueryable<Order> ApplyDatePosition(
        IQueryable<Order> orders, bool descending, string position, Guid id)
    {
        if (!long.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw InvalidOperationalPositionCursor();
        }

        var value = new DateTime(ticks, DateTimeKind.Utc);
        return descending
            ? orders.Where(order => order.OrderDate < value
                || (order.OrderDate == value && order.Id.CompareTo(id) > 0))
            : orders.Where(order => order.OrderDate > value
                || (order.OrderDate == value && order.Id.CompareTo(id) > 0));
    }

    private static IQueryable<Order> ApplyStringPosition(
        IQueryable<Order> orders,
        bool descending,
        string position,
        Guid id,
        Expression<Func<Order, string>> selector)
    {
        var parameter = selector.Parameters[0];
        var body = selector.Body;
        var value = Expression.Constant(position);
        var comparison = Expression.Call(body, nameof(string.CompareTo), Type.EmptyTypes, value);
        var equal = Expression.Equal(body, value);
        var idAfter = Expression.GreaterThan(
            Expression.Call(Expression.Property(parameter, nameof(Order.Id)), nameof(Guid.CompareTo), Type.EmptyTypes,
                Expression.Constant(id)),
            Expression.Constant(0));
        var keyAfter = Expression.LessThan(comparison, Expression.Constant(0));
        if (!descending)
        {
            keyAfter = Expression.GreaterThan(comparison, Expression.Constant(0));
        }

        var predicate = Expression.Lambda<Func<Order, bool>>(
            Expression.OrElse(keyAfter, Expression.AndAlso(equal, idAfter)), parameter);
        return orders.Where(predicate);
    }

    private static IQueryable<Order> ApplyDecimalPosition(
        IQueryable<Order> orders, bool descending, string position, Guid id)
    {
        if (!decimal.TryParse(position, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw InvalidOperationalPositionCursor();
        }

        return descending
            ? orders.Where(order => order.Total < value
                || (order.Total == value && order.Id.CompareTo(id) > 0))
            : orders.Where(order => order.Total > value
                || (order.Total == value && order.Id.CompareTo(id) > 0));
    }

    private static IQueryable<Order> ApplyEnumPosition<TEnum>(
        IQueryable<Order> orders,
        bool descending,
        string position,
        Guid id,
        Expression<Func<Order, TEnum>> selector)
        where TEnum : struct, Enum
    {
        if (!int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
            || !Enum.IsDefined(typeof(TEnum), raw))
        {
            throw InvalidOperationalPositionCursor();
        }

        var value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
        var parameter = selector.Parameters[0];
        var body = selector.Body;
        var equal = Expression.Equal(body, Expression.Constant(value));
        var comparison = Expression.GreaterThan(
            Expression.Convert(body, typeof(int)), Expression.Constant(raw));
        if (descending)
        {
            comparison = Expression.LessThan(
                Expression.Convert(body, typeof(int)), Expression.Constant(raw));
        }

        var idAfter = Expression.GreaterThan(
            Expression.Call(Expression.Property(parameter, nameof(Order.Id)), nameof(Guid.CompareTo), Type.EmptyTypes,
                Expression.Constant(id)),
            Expression.Constant(0));
        var predicate = Expression.Lambda<Func<Order, bool>>(
            Expression.OrElse(comparison, Expression.AndAlso(equal, idAfter)), parameter);
        return orders.Where(predicate);
    }

    private static string NormalizeList(string? value) =>
        string.Join(',', (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant()).OrderBy(token => token));

    private static string NormalizeScalar(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string FormatInstant(DateTime? value) =>
        QueryInstant.AsUtc(value)?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

}
