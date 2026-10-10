using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Settings;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IBasketItemFactory"/>. <c>BuildRegularItemAsync</c> is a faithful
/// extraction of the non-menu item-creation branch of <c>BasketService.AddItemToBasketAsync</c>;
/// behaviour is unchanged. It resolves side-item prices from the database, so it depends on
/// <see cref="ApplicationDbContext"/>; the ingredient customisation state (price + quantities JSON)
/// is delegated to the single shared <see cref="ILineCustomizationBuilder"/>.
/// </summary>
public partial class BasketItemFactory : IBasketItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILineCustomizationBuilder _lineCustomizationBuilder;
    private readonly int _maxQuantityPerItem;

    public BasketItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILineCustomizationBuilder lineCustomizationBuilder,
        IOptions<BasketSettings> basketSettings)
    {
        _context = context;
        _currentUserService = currentUserService;
        _lineCustomizationBuilder = lineCustomizationBuilder;
        _maxQuantityPerItem = basketSettings.Value.MaxQuantityPerItem;
    }

    public void EnsureAtLeastMinimum(BasketItem line) =>
        _lineCustomizationBuilder.EnsureAtLeastMinimum(line);

    public async Task<BasketItem> BuildRegularItemAsync(
        Product product, ProductVariation? variation, AddToBasketDto item, Guid basketId, OrderType? basketOrderType)
    {
        var explicitSelection = ExplicitCustomizationSelection.Resolve(product, item.CustomizationSelections);
        var (selectedIngredients, ingredientQuantities) = ResolveIngredientSelection(
            product, explicitSelection, item.SelectedIngredients, item.IngredientQuantities);

        foreach (var option in explicitSelection.ProductOptions)
        {
            BasketComponentGuard.EnsureNotOrderedAlone(option.Product);
            BasketChannelGuard.EnsureOrderable(option.Product, basketOrderType);
        }

        var customerSteps = CustomerStepManifestStore.Read(product)?.Steps;
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);
        var selectedVariationStep = customerSteps?.FirstOrDefault(step =>
            step.Kind == CustomerStepKind.ProductVariation
            && step.TargetId == variation?.Id
            && step.CompositionRole == CompositionRole.Dish);

        // Ingredient customization (price + quantities JSON) via the single shared writer, so the
        // regular and bundle-child paths can never diverge on a new field. Regular items keep the
        // verbatim-client-map precedence; the sauce allowance (plan D10) is this product's own.
        var customization = _lineCustomizationBuilder.Build(
            product.DetailedIngredients, selectedIngredients,
            ingredientQuantities, preferProvidedQuantities: true,
            options: LineCustomizationOptions.FromProduct(product));
        decimal customizationPrice = customization.CustomizationPrice;
        customizationPrice += explicitSelection.ProductOptions.Sum(option => option.AdditionalPrice);

        var sides = await BuildRegularSideItemsAsync(
            product, item.SelectedSideItems, customerSteps, basketOrderType);
        customizationPrice += sides.Price;

        var basketItem = new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            QuantityBasis = QuantityBasis.LineTotal,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = CompositionRole.Dish,
            PresentationLabel = selectedVariationStep?.PresentationLabel,
            Quantity = item.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice + customizationPrice) * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            SelectedIngredients = customization.SelectedIngredients,
            AddedIngredients = item.AddedIngredients,
            IngredientQuantitiesJson = customization.IngredientQuantitiesJson,
            IngredientCompositionRolesJson = SerializeIngredientRoles(
                CustomerStepManifestStore.IngredientRolesFor(customerSteps, product.Id)),
            CustomizationPrice = customizationPrice,
            SelectedSideItemsJson = sides.SerializedSelections,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        foreach (var option in explicitSelection.ProductOptions)
        {
            basketItem.ChildBasketItems.Add(new BasketItem
            {
                BasketId = basketId,
                ProductId = option.Product.Id,
                ParentBasketItem = basketItem,
                ProductCustomizationOptionId = option.MembershipId,
                Quantity = item.Quantity,
                UnitPrice = option.AdditionalPrice,
                ItemTotal = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            });
        }

        if (basketItem.ChildBasketItems.Count > 0)
        {
            basketItem.UnitPrice += customizationPrice;
            basketItem.ItemTotal = basketItem.UnitPrice * item.Quantity;
        }

        return basketItem;
    }

    private async Task<SideItemBuildResult> BuildRegularSideItemsAsync(
        Product product,
        List<SelectedSideItemDto>? requestedSelections,
        IReadOnlyList<CustomerStepManifestStepDto>? customerSteps,
        OrderType? basketOrderType)
    {
        var selections = requestedSelections?.Where(selection => selection.Quantity > 0).ToList();
        if (selections is not { Count: > 0 })
            return await BuildEmptySideSelectionAsync(product.Id);

        var memberships = await LoadSideMembershipsAsync(product.Id);
        var resolvedSelections = ResolveSideMemberships(selections, memberships, customerSteps);
        EnsureRequiredSideMemberships(memberships, resolvedSelections);
        var sideProducts = await LoadSideProductsAsync(selections);
        EnsureSideProductsOrderable(sideProducts, basketOrderType);
        return PriceVerifiedSideSelections(resolvedSelections, memberships.Count, sideProducts);
    }

    private async Task<SideItemBuildResult> BuildEmptySideSelectionAsync(Guid productId)
    {
        var hasRequiredSide = await _context.ProductSideItems.AsNoTracking()
            .AnyAsync(row => row.MainProductId == productId && row.IsRequired);
        if (hasRequiredSide)
            throw new BadRequestException("This product requires one or more suggested side items.");
        return new(0m, null);
    }

    private async Task<List<ProductSideItem>> LoadSideMembershipsAsync(Guid productId) =>
        await _context.ProductSideItems.AsNoTracking()
            .Where(row => row.MainProductId == productId)
            .ToListAsync();

    private static List<SelectedSideItemDto> ResolveSideMemberships(
        List<SelectedSideItemDto> selections,
        List<ProductSideItem> memberships,
        IReadOnlyList<CustomerStepManifestStepDto>? customerSteps)
    {
        var resolved = new List<SelectedSideItemDto>(selections.Count);
        foreach (var selection in selections)
        {
            var membership = ResolveSideMembership(selection, memberships);
            var legacyUnassociatedSelection = membership is null
                && selection.SuggestedSideItemId is null
                && memberships.Count == 0;
            if (membership is null && !legacyUnassociatedSelection)
                throw new BadRequestException("A selected side is stale or ambiguous; refresh the product and try again.");
            var step = FindRegularSideStep(customerSteps, membership?.Id);
            resolved.Add(selection with
            {
                SuggestedSideItemId = membership?.Id,
                PresentationOrder = membership?.DisplayOrder,
                CompositionRole = step?.CompositionRole
                    ?? (membership is null ? CompositionRole.Unknown : CompositionRole.Side),
            });
        }
        return resolved;
    }

    private static ProductSideItem? ResolveSideMembership(
        SelectedSideItemDto selection, List<ProductSideItem> memberships)
    {
        var candidates = memberships.Where(row => row.SideItemProductId == selection.Id).ToList();
        if (selection.SuggestedSideItemId is Guid associationId)
            return candidates.SingleOrDefault(row => row.Id == associationId);
        if (candidates.Count == 1)
            return candidates[0];
        return null;
    }

    private static CustomerStepManifestStepDto? FindRegularSideStep(
        IReadOnlyList<CustomerStepManifestStepDto>? steps, Guid? membershipId) =>
        steps?.FirstOrDefault(candidate =>
            membershipId.HasValue
            && candidate.Kind == CustomerStepKind.ProductSuggestedSide
            && candidate.TargetId == membershipId);

    private static void EnsureRequiredSideMemberships(
        List<ProductSideItem> memberships,
        IReadOnlyList<SelectedSideItemDto> resolvedSelections)
    {
        var selectedIds = resolvedSelections.Where(selection => selection.SuggestedSideItemId.HasValue)
            .Select(selection => selection.SuggestedSideItemId.GetValueOrDefault()).ToHashSet();
        if (memberships.Any(row => row.IsRequired && !selectedIds.Contains(row.Id)))
            throw new BadRequestException("This product requires one or more suggested side items.");
    }

    private async Task<Dictionary<Guid, Product>> LoadSideProductsAsync(
        IReadOnlyList<SelectedSideItemDto> selections)
    {
        var productIds = selections.Select(selection => selection.Id).Distinct().ToList();
        return await _context.Products.AsNoTracking().AsSplitQuery()
            // ProductCategories -> Category is what makes the channel guard meaningful for
            // inheriting products; unloaded categories would resolve as unrestricted.
            .Include(product => product.ProductCategories)
                .ThenInclude(category => category.Category)
            .Include(product => product.Variations)
            .Where(product => productIds.Contains(product.Id) && product.IsActive && product.IsAvailable)
            .ToDictionaryAsync(product => product.Id);
    }

    private static void EnsureSideProductsOrderable(
        Dictionary<Guid, Product> sideProducts, OrderType? basketOrderType)
    {
        foreach (var sideProduct in sideProducts.Values)
        {
            BasketComponentGuard.EnsureNotOrderedAlone(sideProduct);
            BasketChannelGuard.EnsureOrderable(sideProduct, basketOrderType);
        }
    }

    private static SideItemBuildResult PriceVerifiedSideSelections(
        List<SelectedSideItemDto> selections,
        int membershipCount,
        Dictionary<Guid, Product> sideProducts)
    {
        var verified = new List<SelectedSideItemDto>(selections.Count);
        var price = 0m;
        foreach (var selection in selections)
        {
            if (!sideProducts.TryGetValue(selection.Id, out var sideProduct))
            {
                HandleUnresolvedSide(selection, membershipCount);
                continue;
            }
            var variation = ResolveSideVariation(sideProduct, selection.ProductVariationId);
            BasketBaseProductGuard.EnsureVariationChosen(sideProduct, variation);
            price += (sideProduct.BasePrice + (variation?.PriceModifier ?? 0m)) * selection.Quantity;
            verified.Add(selection);
        }

        var serialized = verified.Count > 0 ? JsonSerializer.Serialize(verified) : null;
        return new(price, serialized);
    }

    private static void HandleUnresolvedSide(SelectedSideItemDto selection, int membershipCount)
    {
        // Older clients could submit unassociated side IDs; historical behavior filtered missing
        // suggestions only when this product had no configured membership rows.
        if (selection.SuggestedSideItemId is null && membershipCount == 0) return;
        throw new BadRequestException("A selected side is no longer available.");
    }

    private static ProductVariation? ResolveSideVariation(Product product, Guid? variationId)
    {
        var variation = variationId.HasValue
            ? product.Variations.FirstOrDefault(row => row.Id == variationId.Value)
            : null;
        if (variationId.HasValue && (variation is null || !variation.IsActive || variation.IsDeleted))
            throw new BadRequestException("The selected side variation is not available.");
        return variation;
    }

    private sealed record SideItemBuildResult(decimal Price, string? SerializedSelections);

    private static string? SerializeIngredientRoles(Dictionary<Guid, CompositionRole>? roles) =>
        roles is { Count: > 0 } ? JsonSerializer.Serialize(roles) : null;

}
