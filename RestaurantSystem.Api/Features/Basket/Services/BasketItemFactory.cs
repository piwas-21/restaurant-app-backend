using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
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
    private readonly ILogger<BasketItemFactory> _logger;

    public BasketItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IBasketPricingService basketPricingService,
        ILogger<BasketItemFactory> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketPricingService = basketPricingService;
        _logger = logger;
    }

    public async Task<BasketItem> BuildRegularItemAsync(Product product, ProductVariation? variation, AddToBasketDto item, Guid basketId)
    {
        // Calculate unit price
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);

        // Customization price from optional-ingredient selections (shared calc — see BasketPricingService).
        decimal customizationPrice = _basketPricingService.CalculateIngredientCustomizationPrice(
            product.DetailedIngredients, item.SelectedIngredients, item.IngredientQuantities);

        // Calculate side items price. Drop non-positive quantities first: side-item
        // quantities are client-supplied, and a negative quantity would otherwise
        // reduce the price (a tampering vector). The filtered list also drives the
        // JSON below, so a 0/negative side item never reaches the basket.
        List<SelectedSideItemDto>? validSideItems = item.SelectedSideItems?
            .Where(s => s.Quantity > 0)
            .ToList();

        if (validSideItems is { Count: > 0 })
        {
            var sideItemIds = validSideItems.Select(s => s.Id).ToList();
            var sideItems = await _context.Products
                .AsNoTracking()
                .Where(p => sideItemIds.Contains(p.Id) && p.IsActive && p.IsAvailable)
                .ToListAsync();

            foreach (var selectedSide in validSideItems)
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
        if (validSideItems is { Count: > 0 })
        {
            selectedSideItemsJson = JsonSerializer.Serialize(validSideItems);
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
            var selectedIngredientIds = item.SelectedIngredients != null
                ? new HashSet<Guid>(item.SelectedIngredients)
                : new HashSet<Guid>();
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

    public async Task<BasketItem> BuildMenuItemAsync(Product product, AddToBasketDto item, Guid basketId)
    {
        if (product.MenuDefinition == null)
            throw new NotFoundException("Menu definition not found");

        // Calculate total price including options
        decimal menuTotalPrice = product.BasePrice;
        var selectedOptions = item.SelectedMenuOptions ?? new List<SelectedMenuOptionDto>();

        // Validate required sections and calculate price
        foreach (var section in product.MenuDefinition.Sections)
        {
            var sectionSelections = selectedOptions.Where(o => o.SectionId == section.Id).ToList();

            // Count distinct items, not sum of quantities
            var distinctItemCount = sectionSelections.Count;

            // Log for debugging
            _logger.LogInformation(
                "Section '{SectionName}' validation: {ItemCount} items selected (min: {Min}, max: {Max})",
                section.Name, distinctItemCount, section.MinSelection, section.MaxSelection
            );

            if (section.IsRequired && distinctItemCount < section.MinSelection)
            {
                throw new BadRequestException($"Section '{section.Name}' requires at least {section.MinSelection} selection(s)");
            }

            if (distinctItemCount > section.MaxSelection)
            {
                throw new BadRequestException($"Section '{section.Name}' allows at most {section.MaxSelection} selection(s)");
            }

            foreach (var selection in sectionSelections)
            {
                // Validate individual selection
                if (selection.Quantity < 1)
                {
                    throw new BadRequestException($"Invalid quantity for item in section '{section.Name}'");
                }

                var sectionItem = section.Items.FirstOrDefault(i => i.ProductId == selection.ItemId);
                if (sectionItem == null)
                    throw new NotFoundException($"Item not found in section '{section.Name}'");

                menuTotalPrice += sectionItem.AdditionalPrice * selection.Quantity;
            }
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        // Create Parent Basket Item
        var basketItem = new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            UnitPrice = menuTotalPrice,
            ItemTotal = menuTotalPrice * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        };

        // Create Child Basket Items for selected options. They are attached to the parent's
        // ChildBasketItems navigation (rather than added to the context here) so the caller
        // persists the whole graph with a single Add — and nothing is saved if any child fails.
        decimal totalCustomizationPrice = 0;

        foreach (var option in selectedOptions)
        {
            var section = product.MenuDefinition.Sections.First(s => s.Id == option.SectionId);
            var sectionItem = section.Items.First(i => i.ProductId == option.ItemId);

            // Load the child product with its ingredients to calculate customization price
            var childProduct = await _context.Products
                .Include(p => p.DetailedIngredients)
                .FirstOrDefaultAsync(p => p.Id == option.ItemId);

            if (childProduct == null)
                throw new NotFoundException($"Child product not found: {option.ItemId}");

            // Customization price for this child item (shared calc — see BasketPricingService).
            decimal childCustomizationPrice = _basketPricingService.CalculateIngredientCustomizationPrice(
                childProduct.DetailedIngredients, option.SelectedIngredients, option.IngredientQuantities);

            // Add child customization price to total
            totalCustomizationPrice += childCustomizationPrice * option.Quantity;

            // Serialize ingredient quantities to JSON for child item
            string? ingredientQuantitiesJson = null;
            if (option.IngredientQuantities != null && option.IngredientQuantities.Count > 0)
            {
                ingredientQuantitiesJson = JsonSerializer.Serialize(option.IngredientQuantities);
            }

            var childItem = new BasketItem
            {
                BasketId = basketId,
                ProductId = option.ItemId, // The actual product ID of the option (e.g., Coke)
                ParentBasketItem = basketItem,
                Quantity = item.Quantity * option.Quantity, // Scale by main item quantity
                UnitPrice = sectionItem.AdditionalPrice, // Section-level additional price
                ItemTotal = 0, // Included in parent total to avoid double counting in recalculation
                CustomizationPrice = childCustomizationPrice, // Store customization price for this child
                SelectedIngredients = option.SelectedIngredients,
                ExcludedIngredients = option.ExcludedIngredients,
                IngredientQuantitiesJson = ingredientQuantitiesJson,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            basketItem.ChildBasketItems.Add(childItem);
        }

        // Update parent item's price to include customization prices from children
        basketItem.UnitPrice = menuTotalPrice + totalCustomizationPrice;
        basketItem.ItemTotal = basketItem.UnitPrice * item.Quantity;
        basketItem.CustomizationPrice = totalCustomizationPrice;

        return basketItem;
    }
}
