using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

public partial class BasketItemFactory
{
    private static List<ResolvedMenuOption> ResolveMenuOptions(
        Product menuProduct, IReadOnlyList<SelectedMenuOptionDto> options)
    {
        var sections = menuProduct.MenuDefinition?.Sections
            ?? throw new NotFoundException("Menu definition not found");
        return options.Select(option => new ResolvedMenuOption(option,
            MenuBundleSelectionRules.ResolveSectionItem(
                sections, option.SectionId, option.ItemId,
                option.ProductVariationId, option.MenuSectionItemId)))
            .ToList();
    }

    private MenuChildrenResult BuildMenuChildren(
        MenuBuildContext context,
        IReadOnlyList<ResolvedMenuOption> options,
        decimal initialMenuPrice)
    {
        var menuPrice = initialMenuPrice;
        var customizationPrice = 0m;
        foreach (var resolved in options)
        {
            var child = BuildMenuChild(context, resolved);
            context.ParentBasketItem.ChildBasketItems.Add(child.Item);
            menuPrice += child.MenuPriceDelta;
            customizationPrice += child.CustomizationPrice;
        }
        return new(menuPrice, customizationPrice);
    }

    private MenuChildResult BuildMenuChild(MenuBuildContext context, ResolvedMenuOption resolved)
    {
        var plan = ResolveMenuChildPlan(context, resolved);
        var childItem = CreateMenuChildBasketItem(context, plan);
        AddMenuCustomizationChildren(context, childItem, plan);
        var variationPrice = !plan.SectionItem.ProductVariationId.HasValue && plan.ComponentVariation is not null
            ? plan.ComponentVariation.PriceModifier * plan.Option.Quantity
            : 0m;
        return new(childItem, variationPrice, plan.CustomizationPrice);
    }

    private MenuChildPlan ResolveMenuChildPlan(
        MenuBuildContext context, ResolvedMenuOption resolved)
    {
        var option = resolved.Selection;
        var sectionItem = resolved.SectionItem;
        var childProduct = GetMenuChildProduct(context.ChildProducts, option.ItemId);
        var componentVariation = BundleComponentSelection.ResolveVariation(
            childProduct, sectionItem, option.ComponentProductVariationId);
        var sectionStep = FindBundleSectionStep(context.CustomerSteps, sectionItem.MenuSectionId);
        var variationStep = FindDishVariationStep(
            context.CustomerSteps, sectionItem, childProduct, componentVariation);
        var parentLabel = ResolveParentLabel(sectionStep?.ParentComponentId, context);
        var explicitSelection = ExplicitCustomizationSelection.Resolve(
            childProduct, option.CustomizationSelections);
        EnsureMenuCustomizationOptionsOrderable(explicitSelection, context.BasketOrderType);

        var (selectedIngredients, ingredientQuantities) = ResolveMenuChildIngredients(
            childProduct, option, explicitSelection);
        var childCustomization = _lineCustomizationBuilder.Build(
            childProduct.DetailedIngredients, selectedIngredients,
            ingredientQuantities, preferProvidedQuantities: false,
            options: LineCustomizationOptions.FromProduct(childProduct));
        var nestedSides = BundleComponentSelection.ResolveSides(
            childProduct, sectionItem.Id, option.SelectedSideItems, context.CustomerSteps,
            context.BasketOrderType, _maxQuantityPerItem);
        var customizationPrice = CalculateMenuChildCustomizationPrice(
            childCustomization.CustomizationPrice, nestedSides, explicitSelection, option.Quantity);

        return new MenuChildPlan
        {
            Option = option,
            SectionItem = sectionItem,
            ChildProduct = childProduct,
            ComponentVariation = componentVariation,
            SectionStep = sectionStep,
            VariationStep = variationStep,
            ParentLabel = parentLabel,
            Customization = childCustomization,
            NestedSides = nestedSides,
            CustomizationPrice = customizationPrice,
            ProductOptions = explicitSelection.ProductOptions
        };
    }

    private static Product GetMenuChildProduct(
        Dictionary<Guid, Product> childProducts, Guid productId)
    {
        if (childProducts.TryGetValue(productId, out var childProduct)) return childProduct;
        throw new NotFoundException($"Child product not found: {productId}");
    }

    private static CustomerStepManifestStepDto? FindBundleSectionStep(
        IReadOnlyList<CustomerStepManifestStepDto>? steps, Guid sectionId) =>
        steps?.FirstOrDefault(step =>
            step.Kind == CustomerStepKind.BundleSection && step.TargetId == sectionId);

    private static CustomerStepManifestStepDto? FindDishVariationStep(
        IReadOnlyList<CustomerStepManifestStepDto>? steps,
        MenuSectionItem sectionItem,
        Product childProduct,
        ProductVariation? variation) =>
        steps?.FirstOrDefault(step =>
            step.Kind == CustomerStepKind.BundleComponentVariation
            && step.SectionItemId == sectionItem.Id
            && step.ProductId == childProduct.Id
            && step.ScopeId == variation?.Id
            && step.CompositionRole == CompositionRole.Dish);

    private static string? ResolveParentLabel(Guid? parentSectionItemId, MenuBuildContext context)
    {
        if (!parentSectionItemId.HasValue) return null;
        if (!context.OptionsByRowId.TryGetValue(parentSectionItemId.Value, out var parentOption))
            throw new BadRequestException("This menu section depends on a selected component that is missing.");
        var parentProductId = parentOption.Selection.ItemId;
        if (!context.ChildProducts.TryGetValue(parentProductId, out var parentProduct))
            throw new NotFoundException($"Parent component product not found: {parentProductId}");
        return parentProduct.Name;
    }

    private static void EnsureMenuCustomizationOptionsOrderable(
        ExplicitCustomizationResolution selection, OrderType? orderType)
    {
        foreach (var selected in selection.ProductOptions)
            BasketChannelGuard.EnsureOrderable(selected.Product, orderType);
    }

    private static (List<Guid>? Ingredients, Dictionary<Guid, int>? Quantities) ResolveMenuChildIngredients(
        Product childProduct,
        SelectedMenuOptionDto option,
        ExplicitCustomizationResolution selection)
    {
        var hasExplicitGroups = childProduct.CustomizationGroups.Any(group => group.IsActive);
        return hasExplicitGroups
            ? (selection.SelectedIngredientIds, selection.IngredientQuantities)
            : (option.SelectedIngredients, option.IngredientQuantities);
    }

    private static decimal CalculateMenuChildCustomizationPrice(
        decimal ingredientPrice,
        IReadOnlyList<(SelectedSideItemDto Selection, decimal UnitPrice)> nestedSides,
        ExplicitCustomizationResolution selection,
        int optionQuantity)
    {
        var sidePrice = nestedSides.Sum(side => side.UnitPrice * side.Selection.Quantity);
        var optionPrice = selection.ProductOptions.Sum(
            selected => selected.AdditionalPrice * selected.Quantity * optionQuantity);
        return (ingredientPrice + sidePrice) * optionQuantity + optionPrice;
    }

    private static BasketItem CreateMenuChildBasketItem(MenuBuildContext context, MenuChildPlan plan)
    {
        var option = plan.Option;
        var sectionItem = plan.SectionItem;
        var scaledQuantity = context.RootItem.Quantity * option.Quantity;
        return new BasketItem
        {
            BasketId = context.ParentBasketItem.BasketId,
            ProductId = option.ItemId,
            ParentBasketItem = context.ParentBasketItem,
            Quantity = scaledQuantity,
            ProductVariationId = plan.ComponentVariation?.Id ?? sectionItem.ProductVariationId,
            SectionId = sectionItem.MenuSectionId,
            MenuSectionItemId = sectionItem.Id,
            ParentComponentMenuSectionItemId = plan.SectionStep?.ParentComponentId,
            QuantityBasis = QuantityBasis.LineTotal,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = plan.SectionStep?.CompositionRole,
            PresentationLabel = ResolveChildPresentationLabel(
                plan.SectionStep, plan.VariationStep, plan.ChildProduct.Name, plan.ParentLabel),
            PresentationOrder = context.ReceiptOrder.GetValueOrDefault(sectionItem.Id),
            UnitPrice = MenuBundleSelectionRules.PriceFor(sectionItem)
                + (!sectionItem.ProductVariationId.HasValue
                    ? plan.ComponentVariation?.PriceModifier ?? 0m
                    : 0m),
            ItemTotal = 0,
            CustomizationPrice = plan.Customization.CustomizationPrice,
            SpecialInstructions = option.SpecialInstructions,
            SelectedIngredients = plan.Customization.SelectedIngredients,
            IngredientQuantitiesJson = plan.Customization.IngredientQuantitiesJson,
            IngredientCompositionRolesJson = SerializeIngredientRoles(
                CustomerStepManifestStore.IngredientRolesFor(
                    context.CustomerSteps, plan.ChildProduct.Id, sectionItem.Id)),
            SelectedSideItemsJson = SerializeNestedSides(plan.NestedSides),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = context.AuditIdentifier
        };
    }

    private static string? ResolveChildPresentationLabel(
        CustomerStepManifestStepDto? sectionStep,
        CustomerStepManifestStepDto? variationStep,
        string childProductName,
        string? parentLabel) =>
        sectionStep?.CompositionRole == CompositionRole.Dish
            ? variationStep?.PresentationLabel ?? sectionStep.PresentationLabel ?? childProductName
            : parentLabel;

    private static string? SerializeNestedSides(
        IReadOnlyList<(SelectedSideItemDto Selection, decimal UnitPrice)> nestedSides)
    {
        if (nestedSides.Count == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(
            nestedSides.Select(side => side.Selection).ToList());
    }

    private static void AddMenuCustomizationChildren(
        MenuBuildContext context, BasketItem childItem, MenuChildPlan plan)
    {
        foreach (var selected in plan.ProductOptions)
        {
            childItem.ChildBasketItems.Add(new BasketItem
            {
                BasketId = context.ParentBasketItem.BasketId,
                ProductId = selected.Product.Id,
                ParentBasketItem = childItem,
                ProductCustomizationOptionId = selected.MembershipId,
                Quantity = context.RootItem.Quantity * plan.Option.Quantity * selected.Quantity,
                UnitPrice = selected.AdditionalPrice,
                ItemTotal = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = context.AuditIdentifier
            });
        }
    }

    private sealed record ResolvedMenuOption(SelectedMenuOptionDto Selection, MenuSectionItem SectionItem);
    private sealed record MenuChildrenResult(decimal MenuPrice, decimal CustomizationPrice);
    private sealed record MenuChildResult(BasketItem Item, decimal MenuPriceDelta, decimal CustomizationPrice);

    private sealed class MenuBuildContext
    {
        public required BasketItem ParentBasketItem { get; init; }
        public required AddToBasketDto RootItem { get; init; }
        public required Dictionary<Guid, ResolvedMenuOption> OptionsByRowId { get; init; }
        public required Dictionary<Guid, Product> ChildProducts { get; init; }
        public required IReadOnlyList<CustomerStepManifestStepDto>? CustomerSteps { get; init; }
        public required IReadOnlyDictionary<Guid, int> ReceiptOrder { get; init; }
        public required OrderType? BasketOrderType { get; init; }
        public required string AuditIdentifier { get; init; }
    }

    private sealed class MenuChildPlan
    {
        public required SelectedMenuOptionDto Option { get; init; }
        public required MenuSectionItem SectionItem { get; init; }
        public required Product ChildProduct { get; init; }
        public required ProductVariation? ComponentVariation { get; init; }
        public required CustomerStepManifestStepDto? SectionStep { get; init; }
        public required CustomerStepManifestStepDto? VariationStep { get; init; }
        public required string? ParentLabel { get; init; }
        public required LineCustomization Customization { get; init; }
        public required IReadOnlyList<(SelectedSideItemDto Selection, decimal UnitPrice)> NestedSides { get; init; }
        public required decimal CustomizationPrice { get; init; }
        public required IReadOnlyList<ResolvedProductCustomization> ProductOptions { get; init; }
    }

}
