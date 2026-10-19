using System.Globalization;
using System.Text.Json;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutQuoteFingerprint
{
    internal static string Compute(CustomerCheckoutQuoteDto quote, CustomerCheckoutReviewDto review, Guid? discountRuleId)
    {
        var money = quote.Money;
        return CustomerBasketOperationHash.Payload(new
        {
            Kind = "customer-checkout-quote-v1",
            quote.SchemaVersion,
            quote.BasketId,
            quote.PurchaseFingerprint,
            quote.Type,
            quote.Currency,
            RequestedPoints = review.PointsToRedeem,
            DiscountRuleId = discountRuleId,
            Money = new
            {
                SubTotal = Amount(money.SubTotal),
                Tax = Amount(money.Tax),
                DeliveryFee = Amount(money.DeliveryFee),
                Discount = Amount(money.Discount),
                CustomerDiscount = Amount(money.CustomerDiscount),
                PointsDiscount = Amount(money.PointsDiscount),
                money.PointsRedeemed,
                Tip = Amount(money.Tip),
                Total = Amount(money.Total)
            },
            Items = quote.Items.OrderBy(item => item.BasketItemId).Select(StableLine).ToArray(),
            Methods = quote.AvailablePaymentMethods.Order().ToArray()
        });
    }

    private static object StableLine(CustomerCheckoutLineDto line) => new
    {
        line.BasketItemId,
        line.ProductId,
        line.MenuId,
        line.ProductVariationId,
        line.Name,
        line.VariationName,
        line.Quantity,
        UnitPrice = Amount(line.UnitPrice),
        ItemTotal = Amount(line.ItemTotal),
        line.SpecialInstructions,
        line.Kind,
        line.QuantityBasis,
        line.ConfigurationScope,
        line.CompositionRole,
        line.PresentationLabel,
        line.PresentationOrder,
        Ingredients = line.Ingredients.OrderBy(item => item.IngredientId).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray(),
        Children = line.Children.Select(StableLine).OrderBy(item => JsonSerializer.Serialize(item), StringComparer.Ordinal).ToArray()
    };

    private static string Amount(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
