using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutProjection
{
    internal static CustomerCheckoutQuoteDto Create(CustomerCheckoutReviewContext context,
        CustomerCheckoutReviewDto review, Order order)
    {
        var basket = context.Basket;
        var roots = order.Items.Where(item => item.ParentOrderItemId is null).ToArray();
        if (order.Items.Count > CustomerCheckoutLimits.MaximumLines || roots.Length != basket.Items.Count
            || basket.Items.Any(item => item.Id is null || item.Id == Guid.Empty)
            || basket.Items.Select(item => item.Id).Distinct().Count() != basket.Items.Count)
            throw Unsupported();
        var lines = CustomerCheckoutComposition.Create(order, basket.Items.Select(item => item.Id).ToArray());
        var money = new CustomerCheckoutMoneyDto(order.SubTotal, order.Tax, order.DeliveryFee,
            order.Discount, order.CustomerDiscountAmount, order.FidelityPointsDiscount,
            order.FidelityPointsRedeemed, order.Tip, order.Total);
        var changed = roots.Where((item, index) => item.ItemTotal != basket.Items[index].ItemTotal).Any()
            || money.DeliveryFee != basket.DeliveryFee || money.Discount != basket.Discount
            || money.CustomerDiscount != basket.CustomerDiscount;
        var quote = new CustomerCheckoutQuoteDto(1, basket.Id, basket.PurchaseFingerprint, string.Empty,
            review.Type, context.Currency, changed, money, lines, context.PaymentMethods);
        return quote with { QuoteFingerprint = CustomerCheckoutQuoteFingerprint.Compute(quote, review, order.CustomerDiscountRuleId) };
    }

    private static ConflictException Unsupported() => new("This basket cannot be reviewed safely. Review it on the restaurant website.");
}
