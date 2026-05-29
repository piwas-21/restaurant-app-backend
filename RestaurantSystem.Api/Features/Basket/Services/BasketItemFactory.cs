using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IBasketItemFactory"/>. <c>BuildRegularItemAsync</c> is a faithful
/// extraction of the non-menu item-creation branch of <c>BasketService.AddItemToBasketAsync</c>;
/// behaviour is unchanged. It resolves side-item prices from the database, so it depends on
/// <see cref="ApplicationDbContext"/>; the ingredient customisation maths is delegated to
/// <see cref="IBasketPricingService"/>.
/// </summary>
public class BasketItemFactory : IBasketItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IBasketPricingService _basketPricingService;

    public BasketItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IBasketPricingService basketPricingService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketPricingService = basketPricingService;
    }

    public async Task<BasketItem> BuildRegularItemAsync(Product product, ProductVariation? variation, AddToBasketDto item, Guid basketId)
    {
        // Calculate unit price
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);

        // Customization price from optional-ingredient selections (shared calc — see BasketPricingService).
        decimal customizationPrice = _basketPricingService.CalculateIngredientCustomizationPrice(
            product.DetailedIngredients, item.SelectedIngredients, item.IngredientQuantities);

        // Calculate side items price
        if (item.SelectedSideItems != null && item.SelectedSideItems.Count > 0)
        {
            var sideItemIds = item.SelectedSideItems.Select(s => s.Id).ToList();
            var sideItems = await _context.Products
                .Where(p => sideItemIds.Contains(p.Id) && p.IsActive && p.IsAvailable)
                .ToListAsync();

            foreach (var selectedSide in item.SelectedSideItems)
            {
                var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                if (sideItem != null)
                {
                    customizationPrice += sideItem.BasePrice * selectedSide.Quantity;
                }
            }
        }

        // Serialize selected side items to JSON
        string? selectedSideItemsJson = null;
        if (item.SelectedSideItems != null && item.SelectedSideItems.Count > 0)
        {
            selectedSideItemsJson = JsonSerializer.Serialize(item.SelectedSideItems);
        }

        // Serialize ingredient quantities to JSON
        // Build from selectedIngredients if ingredientQuantities wasn't provided
        string? ingredientQuantitiesJson = null;
        if (item.IngredientQuantities != null && item.IngredientQuantities.Count > 0)
        {
            ingredientQuantitiesJson = JsonSerializer.Serialize(item.IngredientQuantities);
        }
        else if (product.DetailedIngredients.Any())
        {
            // Build ingredientQuantities from selectedIngredients
            // This ensures kitchen prints can show "NO xxx" for deselected ingredients
            var selectedIngredientIds = item.SelectedIngredients ?? new List<Guid>();
            var builtQuantities = new Dictionary<Guid, int>();

            foreach (var ingredient in product.DetailedIngredients.Where(i => i.IsActive))
            {
                bool isSelected = selectedIngredientIds.Contains(ingredient.Id);

                if (isSelected)
                {
                    // Selected ingredient: quantity 1 (or from ingredientQuantities if provided)
                    builtQuantities[ingredient.Id] = 1;
                }
                else if (ingredient.IsOptional || ingredient.IsIncludedInBasePrice)
                {
                    // Optional ingredient not selected: mark as deselected (quantity 0)
                    builtQuantities[ingredient.Id] = 0;
                }
                // Non-optional ingredients that are not selected are implicitly included
            }

            if (builtQuantities.Count > 0)
            {
                ingredientQuantitiesJson = JsonSerializer.Serialize(builtQuantities);
            }
        }

        return new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            Quantity = item.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice + customizationPrice) * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            SelectedIngredients = item.SelectedIngredients,
            ExcludedIngredients = item.ExcludedIngredients,
            AddedIngredients = item.AddedIngredients,
            IngredientQuantitiesJson = ingredientQuantitiesJson,
            CustomizationPrice = customizationPrice,
            SelectedSideItemsJson = selectedSideItemsJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };
    }
}
