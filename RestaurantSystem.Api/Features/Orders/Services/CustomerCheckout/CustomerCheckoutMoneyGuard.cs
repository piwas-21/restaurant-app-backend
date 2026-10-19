using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutMoneyGuard
{
    internal static void Ensure(Order order)
    {
        // These columns are decimal(10,2). A zero final total must not conceal a subtotal or
        // line amount that order acceptance could not persist. Individual percentage-discount
        // amounts use unrestricted numeric columns and deliberately retain their exact scale.
        var columns = new[] { order.SubTotal, order.Tax, order.DeliveryFee, order.Discount, order.Tip, order.Total };
        if (columns.Any(value => !FitsColumn(value))
            || order.CustomerDiscountAmount is < 0 or > OrderFieldLimits.MaximumMoneyAmount
            || order.FidelityPointsDiscount is < 0 or > OrderFieldLimits.MaximumMoneyAmount
            || order.Items.Any(item => !FitsColumn(item.ItemTotal)
                || !FitsColumn(item.UnitPrice, signed: item.ParentOrderItemId.HasValue)))
            throw new BadRequestException("The checkout amount is unsupported. Ask the restaurant for help.");
    }

    private static bool FitsColumn(decimal value, bool signed = false) =>
        value >= (signed ? -OrderFieldLimits.MaximumMoneyAmount : 0m)
        && value <= OrderFieldLimits.MaximumMoneyAmount && decimal.Round(value, 2) == value;
}
