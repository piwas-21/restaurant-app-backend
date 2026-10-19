using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutComposition
{
    internal static IReadOnlyList<CustomerCheckoutLineDto> Create(Order order, IReadOnlyList<Guid?>? basketRootIds = null)
    {
        var roots = order.Items.Where(item => item.ParentOrderItemId is null).ToArray();
        if (roots.Length == 0 || order.Items.Count > CustomerCheckoutLimits.MaximumLines
            || basketRootIds is not null && basketRootIds.Count != roots.Length) throw Unsupported();
        var children = order.Items.Where(item => item.ParentOrderItemId.HasValue)
            .ToLookup(item => item.ParentComponentOrderItemId ?? item.ParentOrderItemId!.Value);
        var seen = new HashSet<Guid>();
        var lines = roots.Select((item, index) => Line(item, basketRootIds?[index], children, seen, 0)).ToArray();
        if (seen.Count != order.Items.Count) throw Unsupported();
        return lines;
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

    private static ConflictException Unsupported() => new("This order composition cannot be displayed safely. Ask the restaurant for help.");
}
