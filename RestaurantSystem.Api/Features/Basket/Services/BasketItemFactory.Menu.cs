using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

public partial class BasketItemFactory
{
    public async Task<BasketItem> BuildMenuItemAsync(
        Product product, AddToBasketDto item, Guid basketId, OrderType? basketOrderType)
    {
        if (product.MenuDefinition == null)
            throw new NotFoundException("Menu definition not found");

        // Calculate total price including options
        decimal menuTotalPrice = product.BasePrice;
        var selectedOptions = item.SelectedMenuOptions ?? new List<SelectedMenuOptionDto>();
        var customerSteps = CustomerStepManifestStore.Read(product)?.Steps;

        // Basket and staff counter orders share one exact section rule. It validates required/min/max
        // counts, quantities, and that every option belongs to the section named by the request.
        menuTotalPrice += MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            product.MenuDefinition.Sections, selectedOptions);

        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        // Create Parent Basket Item
        var basketItem = new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            Quantity = item.Quantity,
            UnitPrice = menuTotalPrice,
            ItemTotal = menuTotalPrice * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            QuantityBasis = QuantityBasis.LineTotal,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = CompositionRole.Menu,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        };

        // Batch-load every selected option's child product (with ingredients) in one query
        // instead of one round-trip per option (avoids N+1).
        var childProductIds = selectedOptions.Select(o => o.ItemId).Distinct().ToList();
        var childProducts = await _context.Products
            .AsSplitQuery()
            .Include(p => p.DetailedIngredients)
            .Include(p => p.Variations)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
                    .ThenInclude(membership => membership.ProductIngredient)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.ProductOptions)
                    // Nested product choices can inherit channel masks from their primary category.
                    .ThenInclude(membership => membership.OptionProduct.ProductCategories)
                        .ThenInclude(category => category.Category)
            // See the side-item load: without the inheritance chain the guard below resolves every
            // inheriting option as unrestricted, which is worse than no guard — it looks like one.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.SuggestedSideItems)
                .ThenInclude(side => side.SideItemProduct)
                    .ThenInclude(sideProduct => sideProduct.ProductCategories)
                        .ThenInclude(category => category.Category)
            .Include(p => p.SuggestedSideItems)
                .ThenInclude(side => side.SideItemProduct)
                    .ThenInclude(sideProduct => sideProduct.Variations)
            .Where(p => childProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // §9.3: a combo being orderable on this channel says nothing about the components chosen
        // inside it, and the caller's guard only ever saw the combo. `OrderChannelGuard` already
        // walks children at order creation, so a line that gets past here is a dead end the guest
        // discovers at checkout rather than at add time.
        //
        // This closes the ADD-under-a-channel half only. The add-then-SWITCH half is still open:
        // `BasketChannelService.FindConflictsAsync` walks root lines only, so a combo added with no
        // channel chosen (permissive by design, and the dominant browse state) still reports zero
        // conflicts when the guest later picks a channel its components refuse. Tracked as §9.15.
        foreach (var childProduct in childProducts.Values)
        {
            BasketChannelGuard.EnsureOrderable(childProduct, basketOrderType);
        }

        // Create Child Basket Items for selected options. They are attached to the parent's
        // ChildBasketItems navigation (rather than added to the context here) so the caller
        // persists the whole graph with a single Add — and nothing is saved if any child fails.
        decimal totalCustomizationPrice = 0;
        var receiptOrder = product.MenuDefinition.Sections
            .OrderBy(section => section.DisplayOrder)
            .SelectMany(section => section.Items.OrderBy(row => row.DisplayOrder))
            .Select((row, index) => (row.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);

        foreach (var option in selectedOptions)
        {
            // The shared rule already validated this exact section/item pair. Resolve through the
            // same helper here so the child row cannot silently drift to another section's price.
            var sectionItem = MenuBundleSelectionRules.ResolveSectionItem(
                product.MenuDefinition.Sections,
                option.SectionId,
                option.ItemId,
                option.ProductVariationId,
                option.MenuSectionItemId);

            if (!childProducts.TryGetValue(option.ItemId, out var childProduct))
                throw new NotFoundException($"Child product not found: {option.ItemId}");
            var componentVariation = BundleComponentSelection.ResolveVariation(
                childProduct, sectionItem, option.ComponentProductVariationId);
            var variationStep = customerSteps?.FirstOrDefault(step =>
                step.Kind == CustomerStepKind.BundleComponentVariation
                && step.SectionItemId == sectionItem.Id
                && step.ProductId == childProduct.Id
                && step.ScopeId == componentVariation?.Id
                && step.CompositionRole == CompositionRole.Dish);
            if (!sectionItem.ProductVariationId.HasValue && componentVariation is not null)
                menuTotalPrice += componentVariation.PriceModifier * option.Quantity;

            var sectionStep = customerSteps?.FirstOrDefault(step =>
                step.Kind == CustomerStepKind.BundleSection && step.TargetId == sectionItem.MenuSectionId);
            var parentOption = sectionStep?.ParentComponentId is Guid parentMenuSectionItemId
                ? selectedOptions.FirstOrDefault(selected =>
                    MenuBundleSelectionRules.ResolveSectionItem(
                        product.MenuDefinition.Sections, selected.SectionId, selected.ItemId,
                        selected.ProductVariationId, selected.MenuSectionItemId).Id == parentMenuSectionItemId)
                : null;
            if (sectionStep?.ParentComponentId.HasValue == true && parentOption is null)
                throw new BadRequestException("This menu section depends on a selected component that is missing.");
            var parentLabel = parentOption is not null && childProducts.TryGetValue(parentOption.ItemId, out var parentProduct)
                ? parentProduct.Name
                : null;

            var explicitSelection = ExplicitCustomizationSelection.Resolve(
                childProduct, option.CustomizationSelections);
            foreach (var selected in explicitSelection.ProductOptions)
            {
                BasketChannelGuard.EnsureOrderable(selected.Product, basketOrderType);
            }
            var hasExplicitGroups = childProduct.CustomizationGroups.Any(group => group.IsActive);
            var selectedIngredients = hasExplicitGroups
                ? explicitSelection.SelectedIngredientIds
                : option.SelectedIngredients;
            var ingredientQuantities = hasExplicitGroups
                ? explicitSelection.IngredientQuantities
                : option.IngredientQuantities;

            // Ingredient customization (price + quantities JSON) via the single shared writer.
            // Bundle children keep the "backfill from the selection when present" precedence so a
            // deselected optional's "NO xxx" reaches the kitchen ticket (issue #150), while an
            // explicit client quantity still wins inside the backfill.
            //
            // S6 decision (plan D10): a bundle option follows the OPTION PRODUCT's own sauce rule,
            // not the parent bundle's — the option IS that product, and the parent bundle owns no
            // sauce rows at all for a per-product allowance to be applied to.
            var childCustomization = _lineCustomizationBuilder.Build(
                childProduct.DetailedIngredients, selectedIngredients,
                ingredientQuantities, preferProvidedQuantities: false,
                options: LineCustomizationOptions.FromProduct(childProduct));
            var nestedSides = BundleComponentSelection.ResolveSides(
                childProduct, sectionItem.Id, option.SelectedSideItems, customerSteps, basketOrderType);
            var nestedSidePrice = nestedSides.Sum(side => side.UnitPrice * side.Selection.Quantity);

            totalCustomizationPrice += childCustomization.CustomizationPrice * option.Quantity;
            totalCustomizationPrice += nestedSidePrice * option.Quantity;
            totalCustomizationPrice += explicitSelection.ProductOptions.Sum(
                selected => selected.AdditionalPrice * selected.Quantity * option.Quantity);

            var childItem = new BasketItem
            {
                BasketId = basketId,
                ProductId = option.ItemId, // The actual product ID of the option (e.g., Coke)
                ParentBasketItem = basketItem,
                Quantity = item.Quantity * option.Quantity, // Scale by main item quantity
                ProductVariationId = componentVariation?.Id ?? sectionItem.ProductVariationId,
                SectionId = sectionItem.MenuSectionId,
                MenuSectionItemId = sectionItem.Id,
                ParentComponentMenuSectionItemId = sectionStep?.ParentComponentId,
                QuantityBasis = QuantityBasis.LineTotal,
                ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
                CompositionRole = sectionStep?.CompositionRole,
                PresentationLabel = sectionStep?.CompositionRole == CompositionRole.Dish
                    ? variationStep?.PresentationLabel ?? sectionStep.PresentationLabel ?? childProduct.Name
                    : parentLabel,
                PresentationOrder = receiptOrder.GetValueOrDefault(sectionItem.Id),
                UnitPrice = MenuBundleSelectionRules.PriceFor(sectionItem)
                    + (!sectionItem.ProductVariationId.HasValue ? componentVariation?.PriceModifier ?? 0m : 0m),
                ItemTotal = 0, // Included in parent total to avoid double counting in recalculation
                CustomizationPrice = childCustomization.CustomizationPrice, // Store customization price for this child
                SpecialInstructions = option.SpecialInstructions,
                SelectedIngredients = childCustomization.SelectedIngredients,
                IngredientQuantitiesJson = childCustomization.IngredientQuantitiesJson,
                IngredientCompositionRolesJson = SerializeIngredientRoles(
                    CustomerStepManifestStore.IngredientRolesFor(customerSteps, childProduct.Id, sectionItem.Id)),
                SelectedSideItemsJson = nestedSides.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(nestedSides.Select(side => side.Selection).ToList())
                    : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            foreach (var selected in explicitSelection.ProductOptions)
            {
                childItem.ChildBasketItems.Add(new BasketItem
                {
                    BasketId = basketId,
                    ProductId = selected.Product.Id,
                    ParentBasketItem = childItem,
                    ProductCustomizationOptionId = selected.MembershipId,
                    Quantity = item.Quantity * option.Quantity * selected.Quantity,
                    UnitPrice = selected.AdditionalPrice,
                    ItemTotal = 0,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = auditIdentifier
                });
            }
            basketItem.ChildBasketItems.Add(childItem);
        }

        basketItem.UnitPrice = menuTotalPrice + totalCustomizationPrice;
        basketItem.ItemTotal = basketItem.UnitPrice * item.Quantity;
        basketItem.CustomizationPrice = totalCustomizationPrice;

        return basketItem;
    }

}
