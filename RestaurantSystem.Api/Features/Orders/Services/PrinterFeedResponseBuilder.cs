using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class PrinterFeedResponseBuilder
{
    public static Dictionary<string, object?> BuildData(
        List<OrderDto> items,
        bool hasMoreOrders,
        string? nextOrderCursor,
        PrinterFeedUpdatesResult updates,
        int projectionVersion)
    {
        var data = new Dictionary<string, object?>
        {
            ["items"] = items,
            ["totalCount"] = items.Count,
            ["page"] = 1,
            ["pageSize"] = PrinterFeedQuery.MaxOrdersPerPoll,
            ["hasMoreOrders"] = hasMoreOrders,
            ["nextOrderCursor"] = nextOrderCursor,
            ["updates"] = updates.Items,
            ["nextUpdateCursor"] = updates.NextUpdateCursor,
            ["hasMoreUpdates"] = updates.HasMoreUpdates
        };
        if (projectionVersion == 2) data["projectionVersion"] = 2;
        return data;
    }

    public static Dictionary<string, object?> BuildErrorData(int? projectionVersion)
    {
        var data = new Dictionary<string, object?>
        {
            ["items"] = Array.Empty<object>(),
            ["totalCount"] = 0,
            ["hasMoreOrders"] = false,
            ["nextOrderCursor"] = null,
            ["updates"] = Array.Empty<object>(),
            ["nextUpdateCursor"] = null,
            ["hasMoreUpdates"] = false
        };
        if (projectionVersion == 2) data["projectionVersion"] = 2;
        return data;
    }
}
