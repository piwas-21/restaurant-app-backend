using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// The order-mail body pieces both order mails need: the line items and the flattened delivery
/// address. Extracted from <see cref="OrderNotificationService"/> when the admin alert moved into
/// its own sender — the guest receipt and the operator alert must render the same order the same
/// way, and two copies of this would drift.
/// </summary>
internal static class OrderEmailComposer
{
    public static List<(string name, int quantity, decimal price)> ComposeItems(OrderDto order) =>
        order.Items.Select(item => (
            name: $"{item.ProductName}{(string.IsNullOrEmpty(item.VariationName) ? "" : $" - {item.VariationName}")}",
            quantity: item.Quantity,
            price: item.ItemTotal
        )).ToList();

    public static string? ComposeDeliveryAddress(OrderDto order)
    {
        if (order.DeliveryAddress == null)
        {
            return null;
        }

        var address = $"{order.DeliveryAddress.AddressLine1}, " +
            $"{order.DeliveryAddress.PostalCode} {order.DeliveryAddress.City}, " +
            $"{order.DeliveryAddress.Country}";

        if (!string.IsNullOrEmpty(order.DeliveryAddress.DeliveryInstructions))
        {
            address += $"\n\nDelivery Instructions: {order.DeliveryAddress.DeliveryInstructions}";
        }

        return address;
    }
}
