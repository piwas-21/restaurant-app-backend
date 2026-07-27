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
public class BasketItemFactory : IBasketItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILineCustomizationBuilder _lineCustomizationBuilder;
    private readonly ILogger<BasketItemFactory> _logger;

    public BasketItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILineCustomizationBuilder lineCustomizationBuilder,
        ILogger<BasketItemFactory> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _lineCustomizationBuilder = lineCustomizationBuilder;
        _logger = logger;
    }

    public async Task<BasketItem> BuildRegularItemAsync(
        Product product, ProductVariation? variation, AddToBasketDto item, Guid basketId, OrderType? basketOrderType)
    {
        // Calculate unit price
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);

        // Ingredient customization (price + quantities JSON) via the single shared writer, so the
        // regular and bundle-child paths can never diverge on a new field. Regular items keep the
        // "explicit client map persisted verbatim" precedence (preferProvidedQuantities: true).
        var customization = _lineCustomizationBuilder.Build(
            product.DetailedIngredients, item.SelectedIngredients, item.ExcludedIngredients,
            item.IngredientQuantities, preferProvidedQuantities: true);
        decimal customizationPrice = customization.CustomizationPrice;

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
                BasketChannelGuard.EnsureOrderable(sideItemProduct, basketOrderType);
            }

            foreach (var selectedSide in validSideItems)
            {
                var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                if (sideItem != null)
                {
                    customizationPrice += sideItem.BasePrice * selectedSide.Quantity;
                }
            }

            selectedSideItemsJson = JsonSerializer.Serialize(validSideItems);
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
            SelectedIngredients = customization.SelectedIngredients,
            ExcludedIngredients = customization.ExcludedIngredients,
            AddedIngredients = item.AddedIngredients,
            IngredientQuantitiesJson = customization.IngredientQuantitiesJson,
            CustomizationPrice = customizationPrice,
            SelectedSideItemsJson = selectedSideItemsJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };
    }

    public async Task<BasketItem> BuildMenuItemAsync(
        Product product, AddToBasketDto item, Guid basketId, OrderType? basketOrderType)
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

        // Batch-load every selected option's child product (with ingredients) in one query
        // instead of one round-trip per option (avoids N+1).
        var childProductIds = selectedOptions.Select(o => o.ItemId).Distinct().ToList();
        var childProducts = await _context.Products
            .Include(p => p.DetailedIngredients)
            // See the side-item load: without the inheritance chain the guard below resolves every
            // inheriting option as unrestricted, which is worse than no guard — it looks like one.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Where(p => childProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // §9.3: a combo being orderable on this channel says nothing about the components chosen
        // inside it, and the caller's guard only ever saw the combo. `OrderChannelGuard` already
        // walks children at order creation, so before this the basket could hold a line the order
        // endpoint would then refuse — a dead end discovered at checkout rather than at add time.
        foreach (var childProduct in childProducts.Values)
        {
            BasketChannelGuard.EnsureOrderable(childProduct, basketOrderType);
        }

        // Create Child Basket Items for selected options. They are attached to the parent's
        // ChildBasketItems navigation (rather than added to the context here) so the caller
        // persists the whole graph with a single Add — and nothing is saved if any child fails.
        decimal totalCustomizationPrice = 0;

        foreach (var option in selectedOptions)
        {
            // Safe lookups: a malformed SectionId/ItemId from the client yields a 400/404,
            // not an unhandled InvalidOperationException (500).
            var section = product.MenuDefinition.Sections.FirstOrDefault(s => s.Id == option.SectionId)
                ?? throw new BadRequestException($"Invalid section '{option.SectionId}' for this menu");
            var sectionItem = section.Items.FirstOrDefault(i => i.ProductId == option.ItemId)
                ?? throw new NotFoundException($"Item not found in section '{section.Name}'");

            if (!childProducts.TryGetValue(option.ItemId, out var childProduct))
                throw new NotFoundException($"Child product not found: {option.ItemId}");

            // Ingredient customization (price + quantities JSON) via the single shared writer.
            // Bundle children keep the "backfill from the selection when present" precedence so a
            // deselected optional's "NO xxx" reaches the kitchen ticket (issue #150), while an
            // explicit client quantity still wins inside the backfill.
            var childCustomization = _lineCustomizationBuilder.Build(
                childProduct.DetailedIngredients, option.SelectedIngredients, option.ExcludedIngredients,
                option.IngredientQuantities, preferProvidedQuantities: false);

            // Add child customization price to total
            totalCustomizationPrice += childCustomization.CustomizationPrice * option.Quantity;

            var childItem = new BasketItem
            {
                BasketId = basketId,
                ProductId = option.ItemId, // The actual product ID of the option (e.g., Coke)
                ParentBasketItem = basketItem,
                Quantity = item.Quantity * option.Quantity, // Scale by main item quantity
                UnitPrice = sectionItem.AdditionalPrice, // Section-level additional price
                ItemTotal = 0, // Included in parent total to avoid double counting in recalculation
                CustomizationPrice = childCustomization.CustomizationPrice, // Store customization price for this child
                SpecialInstructions = option.SpecialInstructions,
                SelectedIngredients = childCustomization.SelectedIngredients,
                ExcludedIngredients = childCustomization.ExcludedIngredients,
                IngredientQuantitiesJson = childCustomization.IngredientQuantitiesJson,
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
