using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed partial class StaffCounterOrderPricing
{
    // Only a server-owned basket may enter here. Restore its selections before the shared live
    // catalogue pricer runs; the legacy translator intentionally carries settled basket prices.
    public async Task<List<CreateOrderItemDto>> PriceBasketAsync(BasketDto basket,
        List<CreateOrderItemDto> translated, CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync(translated, cancellationToken, includeSides: true);
        for (var index = 0; index < basket.Items.Count; index++)
            RestoreBasketLine(basket.Items[index], translated[index], products);
        return translated.Select(item => PriceLine(item, products, sourceIsBasket: true)).ToList();
    }

    private static void RestoreBasketLine(BasketItemDto source, CreateOrderItemDto target,
        IReadOnlyDictionary<Guid, Product> products)
    {
        target.SelectedIngredientIds = source.SelectedIngredients?.ToList();
        if (source.ProductId is not Guid productId || !products.TryGetValue(productId, out var product)) return;
        ValidateBasketIngredients(source, product);
        ValidateBasketSides(source, product);
        RestoreProductCustomizations(source, target, product);
        RestoreNestedBasketLines(source, target, products);
    }

    private static void RestoreProductCustomizations(BasketItemDto source, CreateOrderItemDto target, Product product)
    {
        var children = source.ChildItems ?? [];
        var customizations = children.Where(child => child.ProductCustomizationOptionId.HasValue).ToArray();
        var groups = product.CustomizationGroups.Where(group => group.IsActive).ToArray();
        if (groups.Length > 0 || customizations.Length > 0)
        {
            if (customizations.Any(child => child.Quantity != source.Quantity || child.ProductVariationId.HasValue
                || child.ChildItems is { Count: > 0 } || child.SelectedSideItems is { Count: > 0 }
                || !string.IsNullOrEmpty(child.SpecialInstructions)))
                throw new BadRequestException("This stored product customization cannot be repriced safely.");
            var optionIds = customizations.Select(child => child.ProductCustomizationOptionId!.Value).ToHashSet();
            if (optionIds.Count != customizations.Length
                || customizations.Any(child => !groups.SelectMany(group => group.ProductOptions)
                    .Any(option => option.Id == child.ProductCustomizationOptionId && option.OptionProductId == child.ProductId)))
                throw new BadRequestException("A stored product customization is unavailable.");
            var sideCount = source.SelectedSideItems?.Count ?? 0;
            target.CustomizationOptionPresentation = children.Select((child, index) => (child, index))
                .Where(pair => pair.child.ProductCustomizationOptionId.HasValue)
                .ToDictionary(pair => pair.child.ProductCustomizationOptionId!.Value,
                    pair => target.ChildItems![sideCount + pair.index]);
            target.CustomizationSelections = ExplicitCustomizationSelection.RestoreSelections(product,
                source.SelectedIngredients, source.IngredientQuantities, optionIds);
        }
    }

    private static void RestoreNestedBasketLines(BasketItemDto source, CreateOrderItemDto target,
        IReadOnlyDictionary<Guid, Product> products)
    {
        var children = source.ChildItems ?? [];
        var sides = source.SelectedSideItems?.Count ?? 0;
        var translated = target.ChildItems;
        if (translated is null) return;
        for (var index = 0; index < sides; index++) RestoreFlatSide(translated[index], products);
        for (var index = 0; index < children.Count; index++)
            if (!children[index].ProductCustomizationOptionId.HasValue)
                RestoreBasketLine(children[index], translated[sides + index], products);
        // The shared pricer generates customization option rows from their current memberships.
        // Keeping the old rows as children would charge them twice or misclassify them as sides.
        target.ChildItems = translated.Where((_, index) => index < sides
            || !children[index - sides].ProductCustomizationOptionId.HasValue).ToList();
    }
    private static void RestoreFlatSide(CreateOrderItemDto side, IReadOnlyDictionary<Guid, Product> products)
    {
        if (side.ProductId is not Guid id || !products.TryGetValue(id, out var product)) return;
        // A flat side has explicit empty answers. Optional groups remain valid; newly required
        // nested choices must be refused rather than guessed or silently selected.
        if (product.CustomizationGroups.Any(group => group.IsActive)) side.CustomizationSelections = [];
        ValidateBasketSides(new() { ProductId = id }, product);
    }

    private static void ValidateBasketIngredients(BasketItemDto source, Product product)
    {
        var active = product.DetailedIngredients.Where(ingredient => ingredient.IsActive)
            .ToDictionary(ingredient => ingredient.Id);
        if ((source.SelectedIngredients ?? []).Any(id => !active.ContainsKey(id))
            || (source.AddedIngredients ?? []).Except(source.SelectedIngredients ?? []).Any())
            throw new BadRequestException("A selected ingredient is no longer available.");
        if ((source.SelectedIngredients ?? []).Any(id => source.IngredientQuantities is not null && source.IngredientQuantities.TryGetValue(id, out var quantity)
            && (quantity < 0 || quantity > active[id].MaxQuantity)))
            throw new BadRequestException("A selected ingredient quantity is no longer available.");
    }

    private static void ValidateBasketSides(BasketItemDto source, Product product)
    {
        var selections = (source.SelectedSideItems ?? []).Select(side => new SelectedSideItemDto
        {
            Id = side.Id,
            SuggestedSideItemId = side.SuggestedSideItemId,
            ProductVariationId = side.ProductVariationId,
            Quantity = side.Quantity
        }).ToList();
        var memberships = product.SuggestedSideItems.ToList();
        // Component sides always require a current association; historical regular unassociated
        // side selections remain compatible when the product has no authored associations.
        if (source.MenuSectionItemId.HasValue && selections.Any(side => !memberships.Any(row =>
            row.SideItemProductId == side.Id && (!side.SuggestedSideItemId.HasValue || row.Id == side.SuggestedSideItemId))))
            throw new BadRequestException("A selected component side is no longer available.");
        var resolved = BasketSideSelectionRules.Resolve(selections, memberships, CustomerStepManifestStore.Read(product)?.Steps);
        BasketSideSelectionRules.EnsureRequired(memberships, resolved);
    }

}
