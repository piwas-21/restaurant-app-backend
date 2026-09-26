using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
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

        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);

        // Ingredient customization (price + quantities JSON) via the single shared writer, so the
        // regular and bundle-child paths can never diverge on a new field. Regular items keep the
        // verbatim-client-map precedence; the sauce allowance (plan D10) is this product's own.
        var customization = _lineCustomizationBuilder.Build(
            product.DetailedIngredients, selectedIngredients,
            ingredientQuantities, preferProvidedQuantities: true,
            sauceIncludedFree: product.SauceIncludedFree, sauceMax: product.SauceMax,
            explicitGroups: product.CustomizationGroups);
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
            var sideItemIds = validSideItems.Select(s => s.Id).ToList();
            var sideItems = await _context.Products
                .AsNoTracking()
                // ProductCategories -> Category is what makes the guard below MEAN anything: an
                // inheriting product with that collection unloaded resolves as UNRESTRICTED, so a
                // guard without this include would permit everything and still look done (the
                // #231/#236/#237/#241 class).
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
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
            var resolvedSides = new List<SelectedSideItemDto>();
            foreach (var selectedSide in validSideItems)
            {
                var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                if (sideItem != null)
                {
                    customizationPrice += sideItem.BasePrice * selectedSide.Quantity;
                    resolvedSides.Add(selectedSide);
                }
            }

            selectedSideItemsJson = resolvedSides.Count > 0 ? JsonSerializer.Serialize(resolvedSides) : null;
        }

        var basketItem = new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            Quantity = item.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice + customizationPrice) * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            SelectedIngredients = customization.SelectedIngredients,
            AddedIngredients = item.AddedIngredients,
            IngredientQuantitiesJson = customization.IngredientQuantitiesJson,
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


}
