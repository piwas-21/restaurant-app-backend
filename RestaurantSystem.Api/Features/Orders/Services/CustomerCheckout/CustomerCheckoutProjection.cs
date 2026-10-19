using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
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
        var children = order.Items.Where(item => item.ParentOrderItemId.HasValue)
            .ToLookup(item => item.ParentComponentOrderItemId ?? item.ParentOrderItemId!.Value);
        var seen = new HashSet<Guid>();
        var lines = roots.Select((item, index) => Line(item, basket.Items[index].Id, children, seen, 0)).ToArray();
        if (seen.Count != order.Items.Count) throw Unsupported();
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

    private static CustomerCheckoutLineDto Line(OrderItem item, Guid? basketItemId,
        ILookup<Guid, OrderItem> children, HashSet<Guid> seen, int depth)
    {
        if (depth > CustomerCheckoutLimits.MaximumDepth || item.Id == Guid.Empty || !seen.Add(item.Id)
            || item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ProductName)) throw Unsupported();
        var ingredients = item.IngredientSnapshots.OrderBy(ingredient => ingredient.PresentationOrder)
            .ThenBy(ingredient => ingredient.IngredientId)
            .Select(ingredient => new CustomerCheckoutIngredientDto(ingredient.IngredientId, ingredient.IngredientName,
                ingredient.Quantity, ingredient.IsRemoved, ingredient.IsAddOn, ingredient.QuantityBasis,
                ingredient.ConfigurationScope, ingredient.CompositionRole, ingredient.PresentationOrder)).ToArray();
        return new(basketItemId, item.ProductId, item.MenuId, item.ProductVariationId, item.ProductName,
            item.VariationName, item.Quantity, item.UnitPrice, item.ItemTotal, item.SpecialInstructions, item.Kind,
            item.QuantityBasis ?? QuantityBasis.Unknown, item.ConfigurationScope ?? ConfigurationScope.Unknown,
            item.CompositionRole ?? CompositionRole.Unknown, item.PresentationLabel, item.PresentationOrder, ingredients,
            children[item.Id].OrderBy(child => child.PresentationOrder)
                .Select(child => Line(child, null, children, seen, depth + 1)).ToArray());
    }

    private static ConflictException Unsupported() => new("This basket cannot be reviewed safely. Review it on the restaurant website.");
}
