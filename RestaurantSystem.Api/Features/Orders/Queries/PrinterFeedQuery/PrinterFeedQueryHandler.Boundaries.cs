using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

public partial class PrinterFeedQueryHandler
{
    private static IQueryable<Order> ApplyModifiedSince(
        IQueryable<Order> ordersQuery, DateTime? modifiedSince, string? deviceId)
    {
        // A cursor with no offset binds Unspecified; normalize it before comparing to timestamptz.
        var modifiedSinceUtc = QueryInstant.AsUtc(modifiedSince);
        return modifiedSinceUtc.HasValue
            ? ordersQuery.Where(o => o.CreatedAt > modifiedSinceUtc.Value
                || (o.UpdatedAt.HasValue && o.UpdatedAt.Value > modifiedSinceUtc.Value)
                || (deviceId != null && o.RoutingStates.Any(state =>
                    state.DeviceId == deviceId && state.Status == DevicePrintStatus.Queued
                    && (state.CreatedAt > modifiedSinceUtc.Value
                        || (state.UpdatedAt.HasValue && state.UpdatedAt.Value > modifiedSinceUtc.Value)))))
            : ordersQuery;
    }

    private static IQueryable<Order> ApplyOrderCursor(IQueryable<Order> ordersQuery, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return ordersQuery;

        var (orderDate, orderId) = PrinterFeedOrderCursor.Decode(cursor);
        return ordersQuery.Where(order => order.OrderDate < orderDate
            || (order.OrderDate == orderDate && order.Id.CompareTo(orderId) > 0));
    }

}
