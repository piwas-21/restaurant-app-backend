using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
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

    public BasketItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILineCustomizationBuilder lineCustomizationBuilder)
    {
        _context = context;
        _currentUserService = currentUserService;
        _lineCustomizationBuilder = lineCustomizationBuilder;
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

        // Calculate side items price. Drop non-positive quantities first: side-item
        // quantities are client-supplied, and a negative quantity would otherwise
        // reduce the price (a tampering vector). The filtered list also drives the
        // JSON below, so a 0/negative side item never reaches the basket.
        List<SelectedSideItemDto>? validSideItems = item.SelectedSideItems?
            .Where(s => s.Quantity > 0)
            .ToList();

        string? selectedSideItemsJson = null;
        if (validSideItems is { Count: > 0 })
        {
            var sideSteps = CustomerStepManifestStore.Read(product)?.Steps;
            var sideItemIds = validSideItems.Select(s => s.Id).ToList();
            var memberships = await _context.ProductSideItems.AsNoTracking()
                .Where(row => row.MainProductId == product.Id)
                .ToListAsync();
            var resolvedSides = new List<SelectedSideItemDto>(validSideItems.Count);
            foreach (var selection in validSideItems)
            {
                var candidates = memberships.Where(row => row.SideItemProductId == selection.Id).ToList();
                var membership = selection.SuggestedSideItemId is Guid associationId
                    ? candidates.SingleOrDefault(row => row.Id == associationId)
                    : candidates.Count == 1 ? candidates[0] : null;
                var legacyUnassociatedSelection = membership is null
                    && selection.SuggestedSideItemId is null
                    && memberships.Count == 0;
                if (membership is null && !legacyUnassociatedSelection)
                    throw new BadRequestException("A selected side is stale or ambiguous; refresh the product and try again.");
                var step = sideSteps?.FirstOrDefault(candidate =>
                    membership is not null
                    && candidate.Kind == CustomerStepKind.ProductSuggestedSide
                    && candidate.TargetId == membership.Id);
                resolvedSides.Add(selection with
                {
                    SuggestedSideItemId = membership?.Id,
                    PresentationOrder = membership?.DisplayOrder,
                    CompositionRole = step?.CompositionRole
                        ?? (membership is null ? CompositionRole.Unknown : CompositionRole.Side),
                });
            }
            var selectedAssociationIds = resolvedSides
                .Where(selection => selection.SuggestedSideItemId.HasValue)
                .Select(selection => selection.SuggestedSideItemId!.Value)
                .ToHashSet();
            if (memberships.Any(row => row.IsRequired && !selectedAssociationIds.Contains(row.Id)))
                throw new BadRequestException("This product requires one or more suggested side items.");

            var sideItems = await _context.Products
                .AsNoTracking()
                // ProductCategories -> Category is what makes the guard below MEAN anything: an
                // inheriting product with that collection unloaded resolves as UNRESTRICTED, so a
                // guard without this include would permit everything and still look done (the
                // #231/#236/#237/#241 class).
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                .Include(p => p.Variations)
                .Where(p => sideItemIds.Contains(p.Id) && p.IsActive && p.IsAvailable)
                .ToListAsync();

            // §9.3: the caller guards the LINE's product; nothing guarded what was attached to it.
            foreach (var sideItemProduct in sideItems)
            {
                BasketComponentGuard.EnsureNotOrderedAlone(sideItemProduct);
                BasketChannelGuard.EnsureOrderable(sideItemProduct, basketOrderType);
            }

            // Persist only the sides that RESOLVED. The guard above can only see what the query
            // returned, so serializing the raw client list would let an unresolved id — a sold-out
            // side (`IsAvailable = false`, a routine pairing with a channel restriction) or one that
            // simply does not exist — reach the line unpriced and unguarded, and from there the
            // kitchen ticket. That is the "stale tab or tampered payload" case this guard exists for.
            var verifiedSides = new List<SelectedSideItemDto>(resolvedSides.Count);
            foreach (var selectedSide in resolvedSides)
            {
                var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                if (sideItem is null)
                    throw new BadRequestException("A selected side is no longer available.");
                var sideVariation = selectedSide.ProductVariationId.HasValue
                    ? sideItem.Variations.FirstOrDefault(row => row.Id == selectedSide.ProductVariationId.Value)
                    : null;
                if (selectedSide.ProductVariationId.HasValue
                    && (sideVariation is null || !sideVariation.IsActive || sideVariation.IsDeleted))
                    throw new BadRequestException("The selected side variation is not available.");
                BasketBaseProductGuard.EnsureVariationChosen(sideItem, sideVariation);
                customizationPrice += (sideItem.BasePrice + (sideVariation?.PriceModifier ?? 0m))
                    * selectedSide.Quantity;
                verifiedSides.Add(selectedSide);
            }

            selectedSideItemsJson = verifiedSides.Count > 0 ? JsonSerializer.Serialize(verifiedSides) : null;
        }
        else if (await _context.ProductSideItems.AsNoTracking()
            .AnyAsync(row => row.MainProductId == product.Id && row.IsRequired))
        {
            throw new BadRequestException("This product requires one or more suggested side items.");
        }

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
            SelectedSideItemsJson = selectedSideItemsJson,
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

    private static string? SerializeIngredientRoles(Dictionary<Guid, CompositionRole>? roles) =>
        roles is { Count: > 0 } ? JsonSerializer.Serialize(roles) : null;

}
