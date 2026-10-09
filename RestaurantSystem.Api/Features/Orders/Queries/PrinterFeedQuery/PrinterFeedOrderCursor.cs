using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

/// <summary>Order pages use descending order date and ascending id; the token carries both
/// keys so bounded pages cannot skip orders sharing the same timestamp.</summary>
internal static class PrinterFeedOrderCursor
{
    public static string Encode(OrderDto order) => PrinterFeedUpdateCursor.Encode(order.OrderDate, order.Id);

    public static (DateTime OrderDate, Guid OrderId) Decode(string value)
    {
        try
        {
            return PrinterFeedUpdateCursor.Decode(value);
        }
        catch (BadRequestException)
        {
            throw new BadRequestException("The order cursor is invalid.");
        }
    }
}
