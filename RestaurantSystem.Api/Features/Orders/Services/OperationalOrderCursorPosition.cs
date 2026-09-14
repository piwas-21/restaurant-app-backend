using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Applies the keyset position carried by an operational queue snapshot cursor.</summary>
internal static partial class OperationalOrderQueryBuilder
{
    private const string OrderNumberSortField = "ordernumber";
    private const string TotalSortField = "total";
    private const string StatusSortField = "status";
    private const string PaymentStatusSortField = "paymentstatus";
    private const string CustomerNameSortField = "customername";
    private const string OrderDateSortField = "orderdate";

    public static IQueryable<Order> ApplyPosition(
        IQueryable<Order> orders,
        string orderBy,
        bool descending,
        string? position,
        Guid? positionId)
    {
        if (position is null || positionId is null)
        {
            return orders;
        }

        var normalized = NormalizeOrderBy(orderBy);
        return normalized switch
        {
            OrderNumberSortField => ApplyStringPosition(orders, descending, position, positionId.Value,
                order => order.OrderNumber),
            TotalSortField => ApplyDecimalPosition(orders, descending, position, positionId.Value),
            StatusSortField => ApplyEnumPosition(orders, descending, position, positionId.Value,
                order => order.Status),
            PaymentStatusSortField => ApplyEnumPosition(orders, descending, position, positionId.Value,
                order => order.PaymentStatus),
            CustomerNameSortField => ApplyStringPosition(orders, descending, position, positionId.Value,
                order => order.CustomerName ?? string.Empty),
            _ => ApplyDatePosition(orders, descending, position, positionId.Value),
        };
    }

    public static IOrderedQueryable<Order> ApplyOrdering(
        IQueryable<Order> orders, string? orderBy, bool descending)
    {
        Expression<Func<Order, object>> keySelector = NormalizeOrderBy(orderBy) switch
        {
            OrderNumberSortField => order => order.OrderNumber,
            TotalSortField => order => order.Total,
            StatusSortField => order => order.Status,
            PaymentStatusSortField => order => order.PaymentStatus,
            CustomerNameSortField => order => order.CustomerName ?? string.Empty,
            _ => order => order.OrderDate,
        };

        return descending
            ? orders.OrderByDescending(keySelector).ThenBy(order => order.Id)
            : orders.OrderBy(keySelector).ThenBy(order => order.Id);
    }

    public static string SortValue(Order order, string? orderBy) =>
        NormalizeOrderBy(orderBy) switch
        {
            OrderNumberSortField => order.OrderNumber,
            TotalSortField => order.Total.ToString("G29", CultureInfo.InvariantCulture),
            StatusSortField => ((int)order.Status).ToString(CultureInfo.InvariantCulture),
            PaymentStatusSortField => ((int)order.PaymentStatus).ToString(CultureInfo.InvariantCulture),
            CustomerNameSortField => order.CustomerName ?? string.Empty,
            _ => order.OrderDate.Ticks.ToString(CultureInfo.InvariantCulture),
        };

    public static string NormalizeOrderBy(string? orderBy) =>
        orderBy?.Trim().ToLowerInvariant() switch
        {
            OrderNumberSortField => OrderNumberSortField,
            TotalSortField => TotalSortField,
            StatusSortField => StatusSortField,
            PaymentStatusSortField => PaymentStatusSortField,
            CustomerNameSortField => CustomerNameSortField,
            _ => OrderDateSortField,
        };

    public static string FilterHash(
        GetOrdersQuery query,
        ICurrentUserService currentUser)
    {
        var canonical = string.Join("\u001f",
            $"scope={query.Scope}",
            $"status={NormalizeList(query.Status)}",
            $"paymentStatus={NormalizeScalar(query.PaymentStatus)}",
            $"orderType={NormalizeScalar(query.OrderType)}",
            $"startDate={FormatInstant(query.StartDate)}",
            $"endDate={FormatInstant(query.EndDate)}",
            $"userId={query.UserId?.ToString("N") ?? string.Empty}",
            $"callerId={currentUser.UserId?.ToString("N") ?? string.Empty}",
            $"staff={currentUser.IsStaff}",
            $"search={query.Search?.ToLowerInvariant() ?? string.Empty}",
            $"isFocus={query.IsFocusOrder?.ToString() ?? string.Empty}",
            $"tenantDay={query.TenantDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty}",
            $"tenantStartDay={query.TenantStartDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty}",
            $"tenantEndDay={query.TenantEndDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty}",
            $"modifiedSince={FormatInstant(query.ModifiedSince)}",
            $"orderBy={NormalizeOrderBy(query.OrderBy)}",
            $"descending={query.Descending}",
            $"tableNumber={query.TableNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }



    private static BadRequestException InvalidOperationalPositionCursor() => new(
        "The operational queue synchronization cursor is invalid.",
        ErrorCodes.InvalidOperationalQueueCursor);
}
