using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

public class BasketService : IBasketService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IBasketPricingService _basketPricingService;
    private readonly IBasketMappingService _basketMappingService;
    private readonly IBasketItemFactory _basketItemFactory;
    private readonly ILogger<BasketService> _logger;

    public BasketService(
       ApplicationDbContext context,
       ICurrentUserService currentUserService,
       IBasketPricingService basketPricingService,
       IBasketMappingService basketMappingService,
       IBasketItemFactory basketItemFactory,
       ILogger<BasketService> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketPricingService = basketPricingService;
        _basketMappingService = basketMappingService;
        _basketItemFactory = basketItemFactory;
        _logger = logger;
    }

    public async Task<BasketDto?> GetBasketAsync(string sessionId, Guid? userId = null)
    {
        // IMPORTANT: Caching disabled to fix race condition where stale basket data
        // could be cached during concurrent add/update operations.
        // The basket query is fast enough (indexed by session/user ID) that
        // caching provides minimal benefit but creates significant consistency issues.

        // Get fresh data from database
        var basket = await GetBasketFromDatabase(sessionId, userId);
        if (basket == null)
            return null;

        var basketDto = await _basketMappingService.MapAsync(basket);

        return basketDto;
    }

    public async Task<BasketDto> AddItemToBasketAsync(string sessionId, Guid? userId, AddToBasketDto item)
    {

        if (item.ProductId == Guid.Empty && item.MenuId == Guid.Empty)
        {
            throw new BadRequestException("Product or Menu should be provided");
        }

        var basket = await GetOrCreateBasketAsync(sessionId, userId);

        if (item.MenuId.HasValue && item.MenuId.Value != Guid.Empty)
        {
            // Existing daily menu logic (keep for backward compatibility if needed, or remove if fully replacing)
            // For now, let's assume we are using the new ProductType.Menu structure via ProductId
        }

        if (item.ProductId != Guid.Empty)
        {
            // Validate product exists and is available
            var product = await _context.Products
                .Include(p => p.Variations)
                .Include(p => p.DetailedIngredients)
                .Include(p => p.MenuDefinition)
                    .ThenInclude(md => md!.Sections)
                        .ThenInclude(s => s.Items)
                            .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive && p.IsAvailable);

            if (product == null)
                throw new NotFoundException("Product not found or unavailable");

            // Handle Menu Type Product
            if (product.Type == ProductType.Menu)
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

                // Create Parent Basket Item
                var basketItem = new BasketItem
                {
                    BasketId = basket.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = menuTotalPrice,
                    ItemTotal = menuTotalPrice * item.Quantity,
                    SpecialInstructions = item.SpecialInstructions,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };

                _context.BasketItems.Add(basketItem);

                // Create Child Basket Items for selected options
                decimal totalCustomizationPrice = 0;
                var childItemsToAdd = new List<BasketItem>();

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
                        BasketId = basket.Id,
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
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    childItemsToAdd.Add(childItem);
                }

                // Update parent item's price to include customization prices from children
                basketItem.UnitPrice = menuTotalPrice + totalCustomizationPrice;
                basketItem.ItemTotal = basketItem.UnitPrice * item.Quantity;
                basketItem.CustomizationPrice = totalCustomizationPrice;

                // Add all child items to context
                foreach (var childItem in childItemsToAdd)
                {
                    _context.BasketItems.Add(childItem);
                }

                await _context.SaveChangesAsync();
                await RecalculateBasketTotalsAsync(basket.Id);
                return await GetBasketAsync(sessionId, userId) ?? throw new BadRequestException("Failed to retrieve basket");
            }

            // Validate variation if specified
            ProductVariation? variation = null;
            if (item.ProductVariationId.HasValue)
            {
                variation = product.Variations.FirstOrDefault(v => v.Id == item.ProductVariationId.Value && v.IsActive);
                if (variation == null)
                    throw new NotFoundException("Product variation not found or unavailable");
            }

            // Check if item with EXACT same customizations already exists in basket
            var existingItem = await _context.BasketItems
                .Where(bi =>
                    bi.BasketId == basket.Id &&
                    bi.ProductId == item.ProductId &&
                    bi.ProductVariationId == item.ProductVariationId)
                .ToListAsync();

            // Find exact match including customizations
            var exactMatch = existingItem.FirstOrDefault(bi =>
            {
                // Compare special instructions
                var sameInstructions = (bi.SpecialInstructions ?? "") == (item.SpecialInstructions ?? "");

                // Compare selected ingredients lists
                var biSelected = bi.SelectedIngredients ?? new List<Guid>();
                var itemSelected = item.SelectedIngredients ?? new List<Guid>();
                var sameSelected = biSelected.Count == itemSelected.Count &&
                                   biSelected.OrderBy(x => x).SequenceEqual(itemSelected.OrderBy(x => x));

                // Compare excluded ingredients lists
                var biExcluded = bi.ExcludedIngredients ?? new List<Guid>();
                var itemExcluded = item.ExcludedIngredients ?? new List<Guid>();
                var sameExcluded = biExcluded.Count == itemExcluded.Count &&
                                   biExcluded.OrderBy(x => x).SequenceEqual(itemExcluded.OrderBy(x => x));

                return sameInstructions && sameSelected && sameExcluded;
            });

            if (exactMatch != null)
            {
                // Update quantity of existing item with same customizations
                exactMatch.Quantity += item.Quantity;
                exactMatch.ItemTotal = exactMatch.Quantity * exactMatch.UnitPrice;
                exactMatch.UpdatedAt = DateTime.UtcNow;
                exactMatch.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }
            else
            {
                var basketItem = await _basketItemFactory.BuildRegularItemAsync(product, variation, item, basket.Id);
                _context.BasketItems.Add(basketItem);
            }
        }

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basket.Id);

        return await GetBasketAsync(sessionId, userId) ?? throw new BadRequestException("Failed to retrieve basket");
    }

    public async Task<BasketDto> UpdateBasketItemAsync(string sessionId, Guid basketItemId, UpdateBasketItemDto update)
    {
        // Get the user's basket first to ensure we're checking the right context
        var userId = _currentUserService.UserId;
        var basket = await GetBasketFromDatabase(sessionId, userId);

        if (basket == null)
            throw new NotFoundException("Basket not found");

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .FirstOrDefaultAsync(bi => bi.Id == basketItemId && bi.BasketId == basket.Id);

        if (basketItem == null)
            throw new NotFoundException("Basket item not found");

        basketItem.Quantity = update.Quantity;
        basketItem.ItemTotal = basketItem.Quantity * basketItem.UnitPrice;
        basketItem.SpecialInstructions = update.SpecialInstructions;
        basketItem.UpdatedAt = DateTime.UtcNow;
        basketItem.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basketItem.BasketId);

        return await GetBasketAsync(sessionId, userId) ?? throw new BadRequestException("Failed to retrieve basket");
    }

    public async Task<BasketDto> RemoveItemFromBasketAsync(string sessionId, Guid basketItemId)
    {
        // Get the user's basket first to ensure we're checking the right context
        var userId = _currentUserService.UserId;
        var basket = await GetBasketFromDatabase(sessionId, userId);

        if (basket == null)
            throw new NotFoundException("Basket not found");

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .Include(bi => bi.ChildBasketItems) // Include child items for cascade deletion
            .FirstOrDefaultAsync(bi => bi.Id == basketItemId && bi.BasketId == basket.Id);

        if (basketItem == null)
            throw new NotFoundException("Basket item not found");

        var basketId = basketItem.BasketId;

        // Remove all child items first (for menu bundles)
        if (basketItem.ChildBasketItems != null && basketItem.ChildBasketItems.Any())
        {
            _context.BasketItems.RemoveRange(basketItem.ChildBasketItems);
        }

        // Remove the parent item
        _context.BasketItems.Remove(basketItem);

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basketId);

        return await GetBasketAsync(sessionId, userId) ?? throw new BadRequestException("Failed to retrieve basket");
    }

    public async Task<BasketDto> ClearBasketAsync(string sessionId)
    {
        // Load the basket with tracking + only the .Items navigation. We can't
        // reuse GetBasketFromDatabase here for two reasons:
        // (1) it loads with AsNoTracking, so scalar mutations don't persist
        //     and child-row deletes via the change tracker don't auto-clear
        //     the in-memory .Items collection (without this, the DTO came
        //     back with items: [...] and Total: 0 — the bug we're fixing);
        // (2) it eager-loads Products / ProductVariations / Menus / etc.
        //     that ClearBasketAsync immediately discards.
        // Filter semantics mirror GetBasketFromDatabase: logged-in users are
        // matched by UserId only; anonymous users by SessionId with no user.
        var userId = _currentUserService.UserId;
        IQueryable<Domain.Entities.Basket> query = _context.Baskets
            .Include(b => b.Items)
            .Where(b => !b.IsDeleted);

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            query = query.Where(b => b.UserId == userId.Value);
        }
        else if (!string.IsNullOrEmpty(sessionId))
        {
            query = query.Where(b => b.SessionId == sessionId && (b.UserId == null || b.UserId == Guid.Empty));
        }
        else
        {
            throw new NotFoundException("Basket not found");
        }

        var basket = await query.FirstOrDefaultAsync();
        if (basket == null)
            throw new NotFoundException("Basket not found");

        _context.BasketItems.RemoveRange(basket.Items);
        basket.Items.Clear();

        // Reset every basket-level field that contributes to totals or that a
        // returning customer would expect to be wiped. Leaving Discount /
        // PromoCode / CustomerDiscount / DeliveryFee / Notes in place would
        // silently re-apply to whatever the customer adds next.
        basket.SubTotal = 0;
        basket.Tax = 0;
        basket.Total = 0;
        basket.Discount = 0;
        basket.CustomerDiscount = 0;
        basket.DeliveryFee = 0;
        basket.PromoCode = null;
        basket.Notes = null;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();

        return await _basketMappingService.MapAsync(basket);
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<BasketDto> ApplyPromoCodeAsync(string sessionId, string promoCode)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        // TODO: Implement promo code logic
        throw new NotImplementedException("Promo code functionality not yet implemented");
    }

    public async Task<BasketDto> RemovePromoCodeAsync(string sessionId)
    {
        var basket = await GetBasketFromDatabase(sessionId, _currentUserService.UserId);
        if (basket == null)
            throw new NotFoundException("Basket not found");

        basket.PromoCode = null;
        basket.Discount = 0;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basket.Id);

        return await GetBasketAsync(sessionId, basket.UserId) ?? throw new BadRequestException("Failed to retrieve basket");
    }

    public async Task<BasketSummaryDto?> GetBasketSummaryAsync(string sessionId, Guid? userId = null)
    {
        var basket = await GetBasketAsync(sessionId, userId);
        if (basket == null)
            return null;

        return new BasketSummaryDto
        {
            Id = basket.Id,
            ItemCount = basket.Items.Sum(i => i.Quantity),
            Total = basket.Total
        };
    }

    public async Task<BasketDto> MergeAnonymousBasketAsync(string sessionId, Guid userId)
    {
        var anonymousBasket = await GetBasketFromDatabase(sessionId, null);
        var userBasket = await GetBasketFromDatabase(null, userId);

        if (anonymousBasket == null)
        {
            return userBasket != null
                ? await _basketMappingService.MapAsync(userBasket)
                : await _basketMappingService.MapAsync(await GetOrCreateBasketAsync(sessionId, userId));
        }

        if (userBasket == null)
        {
            // Assign anonymous basket to user
            anonymousBasket.UserId = userId;
            anonymousBasket.UpdatedAt = DateTime.UtcNow;
            anonymousBasket.UpdatedBy = userId.ToString();
            await _context.SaveChangesAsync();

            return await _basketMappingService.MapAsync(anonymousBasket);
        }

        // Merge anonymous items into user basket
        var anonymousItems = await _context.BasketItems
            .Where(bi => bi.BasketId == anonymousBasket.Id)
            .ToListAsync();

        foreach (var item in anonymousItems)
        {
            var existingItem = await _context.BasketItems
                .FirstOrDefaultAsync(bi =>
                    bi.BasketId == userBasket.Id &&
                    bi.ProductId == item.ProductId &&
                    bi.ProductVariationId == item.ProductVariationId);

            if (existingItem != null)
            {
                existingItem.Quantity += item.Quantity;
                existingItem.ItemTotal = existingItem.Quantity * existingItem.UnitPrice;
                existingItem.UpdatedAt = DateTime.UtcNow;
                existingItem.UpdatedBy = userId.ToString();
            }
            else
            {
                item.BasketId = userBasket.Id;
                item.UpdatedAt = DateTime.UtcNow;
                item.UpdatedBy = userId.ToString();
            }
        }

        // Delete anonymous basket
        anonymousBasket.IsDeleted = true;
        anonymousBasket.DeletedAt = DateTime.UtcNow;
        anonymousBasket.DeletedBy = userId.ToString();

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(userBasket.Id);

        return await GetBasketAsync(sessionId, userId) ?? throw new BadRequestException("Failed to retrieve basket");
    }

    public async Task RecalculateBasketTotalsAsync(Guid basketId)
    {
        var basket = await _context.Baskets
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == basketId);

        if (basket == null)
            return;

        // Pricing (sub-total, customer discount, tax, total) is computed by the
        // dedicated BasketPricingService; persistence stays here.
        await _basketPricingService.ApplyTotalsAsync(basket);

        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
    }

    private async Task<Domain.Entities.Basket> GetOrCreateBasketAsync(string? sessionId, Guid? userId)
    {
        var basket = await GetBasketFromDatabase(sessionId, userId);

        if (basket == null)
        {
            basket = new Domain.Entities.Basket
            {
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId?.ToString() ?? "System"
            };

            _context.Baskets.Add(basket);
            await _context.SaveChangesAsync();
        }

        return basket;
    }

    private async Task<Domain.Entities.Basket?> GetBasketFromDatabase(string? sessionId, Guid? userId)
    {
        var query = _context.Baskets
            .AsNoTracking() // Ensure fresh data without EF Core tracking interference
            .AsSplitQuery() // Prevent cartesian explosion with multiple Includes
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.ProductVariation)
                    .ThenInclude(pv => pv!.Descriptions)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Menu)
                .ThenInclude(b => b!.MenuItems)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.ChildBasketItems)
                    .ThenInclude(c => c.Product)
            .Include(b => b.Items)
            .Where(b => !b.IsDeleted);

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            query = query.Where(b => b.UserId == userId.Value);
        }
        else if (!string.IsNullOrEmpty(sessionId))
        {
            query = query.Where(b => b.SessionId == sessionId && (b.UserId == null || b.UserId == Guid.Empty));
        }
        else
        {
            return null;
        }

        return await query.FirstOrDefaultAsync();
    }

}
