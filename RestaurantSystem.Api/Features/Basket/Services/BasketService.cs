using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

public class BasketService : IBasketService
{
    private readonly ApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly ITaxConfigurationService _taxConfigurationService;
    private readonly ILogger<BasketService> _logger;
    private readonly IConfiguration _configuration;

    private const string BASKET_CACHE_KEY_PREFIX = "basket:";
    private const int BASKET_CACHE_EXPIRY_MINUTES = 30;

    public BasketService(
       ApplicationDbContext context,
       IDistributedCache cache,
       ICurrentUserService currentUserService,
       ICustomerDiscountService customerDiscountService,
       ITaxConfigurationService taxConfigurationService,
       ILogger<BasketService> logger,
       IConfiguration configuration)
    {
        _context = context;
        _cache = cache;
        _currentUserService = currentUserService;
        _customerDiscountService = customerDiscountService;
        _taxConfigurationService = taxConfigurationService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<BasketDto?> GetBasketAsync(string sessionId, Guid? userId = null)
    {
        // Try to get from cache first
        var cacheKey = GetCacheKey(sessionId, userId);
        var cachedBasket = await _cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedBasket))
        {
            return JsonSerializer.Deserialize<BasketDto>(cachedBasket);
        }

        // Get from database
        var basket = await GetBasketFromDatabase(sessionId, userId);
        if (basket == null)
            return null;

        var basketDto = await MapToBasketDtoAsync(basket);

        // Cache the result
        await CacheBasketAsync(cacheKey, basketDto);

        return basketDto;
    }

    public async Task<BasketDto> AddItemToBasketAsync(string sessionId, Guid? userId, AddToBasketDto item)
    {

        if (item.ProductId == Guid.Empty && item.MenuId == Guid.Empty)
        {
            throw new InvalidOperationException("Product or Menu should be provided");
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
                throw new InvalidOperationException("Product not found or unavailable");

            // Handle Menu Type Product
            if (product.Type == ProductType.Menu)
            {
                if (product.MenuDefinition == null)
                    throw new InvalidOperationException("Menu definition not found");

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
                        throw new InvalidOperationException($"Section '{section.Name}' requires at least {section.MinSelection} selection(s)");
                    }
                    
                    if (distinctItemCount > section.MaxSelection)
                    {
                        throw new InvalidOperationException($"Section '{section.Name}' allows at most {section.MaxSelection} selection(s)");
                    }
                    
                    foreach (var selection in sectionSelections)
                    {
                        // Validate individual selection
                        if (selection.Quantity < 1)
                        {
                            throw new InvalidOperationException($"Invalid quantity for item in section '{section.Name}'");
                        }
                        
                        var sectionItem = section.Items.FirstOrDefault(i => i.ProductId == selection.ItemId);
                        if (sectionItem == null)
                            throw new InvalidOperationException($"Item not found in section '{section.Name}'");
                            
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
                    CreatedBy = _currentUserService.UserId?.ToString() ?? "System"
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
                        throw new InvalidOperationException($"Child product not found: {option.ItemId}");
                    
                    // Calculate customization price for this child item based on selected ingredients
                    decimal childCustomizationPrice = 0;
                    if (childProduct.DetailedIngredients != null && childProduct.DetailedIngredients.Any())
                    {
                        var selectedIngredientIds = option.SelectedIngredients ?? new List<Guid>();
                        
                        foreach (var ingredient in childProduct.DetailedIngredients.Where(i => i.IsOptional && i.IsActive))
                        {
                            bool isSelected = selectedIngredientIds.Contains(ingredient.Id);
                            int quantity = 1;
                            
                            if (option.IngredientQuantities != null && option.IngredientQuantities.TryGetValue(ingredient.Id, out var qty))
                            {
                                quantity = qty;
                            }
                            
                            // Validate max quantity
                            if (quantity > ingredient.MaxQuantity)
                            {
                                quantity = ingredient.MaxQuantity;
                            }
                            
                            if (ingredient.IsIncludedInBasePrice)
                            {
                                // Ingredient price is included in base price for 1 quantity
                                if (!isSelected)
                                {
                                    // Deselected: deduct the included quantity (1)
                                    childCustomizationPrice -= ingredient.Price;
                                }
                                else if (quantity > 1)
                                {
                                    // Selected with more than 1: add extra quantities beyond the free one
                                    childCustomizationPrice += ingredient.Price * (quantity - 1);
                                }
                                // quantity == 1: already in base price, no change
                            }
                            else
                            {
                                // Regular optional ingredient (not included in base)
                                // Add price if user selected it
                                if (isSelected)
                                {
                                    childCustomizationPrice += ingredient.Price * quantity;
                                }
                            }
                        }
                    }
                    
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
                        CreatedBy = _currentUserService.UserId?.ToString() ?? "System"
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
                await InvalidateBasketCacheAsync(sessionId, userId);
                return await GetBasketAsync(sessionId, userId) ?? throw new InvalidOperationException("Failed to retrieve basket");
            }

            // Validate variation if specified
            ProductVariation? variation = null;
            if (item.ProductVariationId.HasValue)
            {
                variation = product.Variations.FirstOrDefault(v => v.Id == item.ProductVariationId.Value && v.IsActive);
                if (variation == null)
                    throw new InvalidOperationException("Product variation not found or unavailable");
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
                exactMatch.UpdatedBy = _currentUserService.UserId?.ToString() ?? "System";
            }
            else
            {
                // Calculate unit price
                var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0);

                // Calculate customization price from optional ingredients
                // Two scenarios:
                // 1. Ingredient is included in base price (IsIncludedInBasePrice = true):
                //    - Base price includes 1 quantity of this ingredient
                //    - Qty 0 (deselected): deduct price * 1
                //    - Qty 1 (selected): no change (already in base)
                //    - Qty 2+: add price * (qty - 1) for extra pieces
                // 2. Ingredient is NOT included in base price (IsIncludedInBasePrice = false):
                //    - If selected by user: add price * qty
                //    - If NOT selected by user: no change
                decimal customizationPrice = 0;
                if (product.DetailedIngredients != null)
                {
                    var selectedIngredientIds = item.SelectedIngredients ?? new List<Guid>();

                    foreach (var ingredient in product.DetailedIngredients.Where(i => i.IsOptional && i.IsActive))
                    {
                        bool isSelected = selectedIngredientIds.Contains(ingredient.Id);
                        int quantity = 1;

                        if (item.IngredientQuantities != null && item.IngredientQuantities.TryGetValue(ingredient.Id, out var qty))
                        {
                            quantity = qty;
                        }

                        // Validate max quantity
                        if (quantity > ingredient.MaxQuantity)
                        {
                            quantity = ingredient.MaxQuantity;
                        }

                        if (ingredient.IsIncludedInBasePrice)
                        {
                            // Ingredient price is included in base price for 1 quantity
                            if (!isSelected)
                            {
                                // Deselected: deduct the included quantity (1)
                                customizationPrice -= ingredient.Price;
                            }
                            else if (quantity > 1)
                            {
                                // Selected with more than 1: add extra quantities beyond the free one
                                customizationPrice += ingredient.Price * (quantity - 1);
                            }
                            // quantity == 1: already in base price, no change
                        }
                        else
                        {
                            // Regular optional ingredient (not included in base)
                            // Add price if user selected it
                            if (isSelected)
                            {
                                customizationPrice += ingredient.Price * quantity;
                            }
                        }
                    }
                }

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
                else if (product.DetailedIngredients != null && product.DetailedIngredients.Any())
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

                // Create new basket item
                var basketItem = new BasketItem
                {
                    BasketId = basket.Id,
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
                    CreatedBy = _currentUserService.UserId?.ToString() ?? "System"
                };

                _context.BasketItems.Add(basketItem);
            }
        }

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basket.Id);

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, userId);

        return await GetBasketAsync(sessionId, userId) ?? throw new InvalidOperationException("Failed to retrieve basket");
    }

    public async Task<BasketDto> UpdateBasketItemAsync(string sessionId, Guid basketItemId, UpdateBasketItemDto update)
    {
        // Get the user's basket first to ensure we're checking the right context
        var userId = _currentUserService.UserId;
        var basket = await GetBasketFromDatabase(sessionId, userId);
        
        if (basket == null)
            throw new InvalidOperationException("Basket not found");

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .FirstOrDefaultAsync(bi => bi.Id == basketItemId && bi.BasketId == basket.Id);

        if (basketItem == null)
            throw new InvalidOperationException("Basket item not found");

        basketItem.Quantity = update.Quantity;
        basketItem.ItemTotal = basketItem.Quantity * basketItem.UnitPrice;
        basketItem.SpecialInstructions = update.SpecialInstructions;
        basketItem.UpdatedAt = DateTime.UtcNow;
        basketItem.UpdatedBy = _currentUserService.UserId?.ToString() ?? "System";

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basketItem.BasketId);

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, userId);

        return await GetBasketAsync(sessionId, userId) ?? throw new InvalidOperationException("Failed to retrieve basket");
    }

    public async Task<BasketDto> RemoveItemFromBasketAsync(string sessionId, Guid basketItemId)
    {
        // Get the user's basket first to ensure we're checking the right context
        var userId = _currentUserService.UserId;
        var basket = await GetBasketFromDatabase(sessionId, userId);
        
        if (basket == null)
            throw new InvalidOperationException("Basket not found");

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .Include(bi => bi.ChildBasketItems) // Include child items for cascade deletion
            .FirstOrDefaultAsync(bi => bi.Id == basketItemId && bi.BasketId == basket.Id);

        if (basketItem == null)
            throw new InvalidOperationException("Basket item not found");

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

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, userId);

        return await GetBasketAsync(sessionId, userId) ?? throw new InvalidOperationException("Failed to retrieve basket");
    }


    public async Task<BasketDto> ClearBasketAsync(string sessionId)
    {
        var basket = await GetBasketFromDatabase(sessionId, _currentUserService.UserId);
        if (basket == null)
            throw new InvalidOperationException("Basket not found");

        // Remove all items
        var items = await _context.BasketItems
            .Where(bi => bi.BasketId == basket.Id)
            .ToListAsync();

        foreach (var item in items)
        {
            _context.BasketItems.Remove(item);
        }

        basket.SubTotal = 0;
        basket.Tax = 0;
        basket.Total = 0;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.UserId?.ToString() ?? "System";

        await _context.SaveChangesAsync();

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, basket.UserId);

        return await MapToBasketDtoAsync(basket);
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
            throw new InvalidOperationException("Basket not found");

        basket.PromoCode = null;
        basket.Discount = 0;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.UserId?.ToString() ?? "System";

        await _context.SaveChangesAsync();
        await RecalculateBasketTotalsAsync(basket.Id);

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, basket.UserId);

        return await GetBasketAsync(sessionId, basket.UserId) ?? throw new InvalidOperationException("Failed to retrieve basket");
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
                ? await MapToBasketDtoAsync(userBasket)
                : await MapToBasketDtoAsync(await GetOrCreateBasketAsync(sessionId, userId));
        }

        if (userBasket == null)
        {
            // Assign anonymous basket to user
            anonymousBasket.UserId = userId;
            anonymousBasket.UpdatedAt = DateTime.UtcNow;
            anonymousBasket.UpdatedBy = userId.ToString();
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateBasketCacheAsync(sessionId, null);
            await InvalidateBasketCacheAsync(sessionId, userId);

            return await MapToBasketDtoAsync(anonymousBasket);
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

        // Invalidate cache
        await InvalidateBasketCacheAsync(sessionId, null);
        await InvalidateBasketCacheAsync(sessionId, userId);

        return await GetBasketAsync(sessionId, userId) ?? throw new InvalidOperationException("Failed to retrieve basket");
    }

    public async Task RecalculateBasketTotalsAsync(Guid basketId)
    {
        var basket = await _context.Baskets
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == basketId);

        if (basket == null)
            return;

        decimal subTotal = 0;

        foreach (var item in basket.Items)
        {
            var itemTotal = item.ItemTotal;
            subTotal += itemTotal;
        }

        basket.SubTotal = subTotal;

        // Calculate customer discount if user is logged in
        decimal customerDiscountAmount = 0;
        bool hasDiscount = false;
        
        if (basket.UserId.HasValue && basket.UserId.Value != Guid.Empty)
        {
            var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
                basket.UserId.Value,
                subTotal
            );

            if (customerDiscount != null)
            {
                customerDiscountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, subTotal);
                hasDiscount = PriceRoundingUtility.HasActiveDiscount(customerDiscountAmount);
                
                _logger.LogInformation(
                    "Applied customer discount '{DiscountName}' (ID: {DiscountId}) to basket {BasketId}: {DiscountAmount:C}",
                    customerDiscount.Name,
                    customerDiscount.Id,
                    basket.Id,
                    customerDiscountAmount
                );
            }
        }

        // Store the customer discount in the basket
        basket.CustomerDiscount = customerDiscountAmount;

        // Tax will be calculated later during order creation when order type is known
        // This is important for Swiss tax compliance (different rates for Dine-In vs Takeaway/Delivery)
        basket.Tax = 0;

        // Calculate total before rounding (without tax since order type is not yet known)
        decimal amountAfterDiscount = basket.SubTotal - customerDiscountAmount - basket.Discount;
        decimal calculatedTotal = amountAfterDiscount + basket.DeliveryFee;
        
        // Apply special rounding for discounted customers
        basket.Total = PriceRoundingUtility.ApplySpecialRounding(calculatedTotal, hasDiscount);
        
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.UserId?.ToString() ?? "System";

        await _context.SaveChangesAsync();
        
        // Invalidate cache after recalculating totals
        await InvalidateBasketCacheAsync(basket.SessionId, basket.UserId);
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
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Product)
                    .ThenInclude(p => p.DetailedIngredients)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.ProductVariation)
                    .ThenInclude(pv => pv!.Descriptions)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Menu)
                .ThenInclude(b => b.MenuItems)
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
            query = query.Where(b => b.SessionId == sessionId && b.UserId == null || b.UserId == Guid.Empty);
        }
        else
        {
            return null;
        }

        return await query.FirstOrDefaultAsync();
    }

    private async Task<BasketDto> MapToBasketDtoAsync(Domain.Entities.Basket basket)
    {
        // Calculate customer discount if user is logged in
        decimal customerDiscountAmount = 0;
        string? customerDiscountName = null;

        if (basket.UserId.HasValue && basket.UserId.Value != Guid.Empty)
        {
            var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
                basket.UserId.Value,
                basket.SubTotal
            );

            if (customerDiscount != null)
            {
                customerDiscountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, basket.SubTotal);
                customerDiscountName = customerDiscount.Name;
            }
        }

        var allItems = (await Task.WhenAll(basket.Items.Select(async item =>
        {
            // Get ingredient names from product's detailed ingredients
            var productIngredients = item.Product?.DetailedIngredients ?? new List<ProductIngredient>();

            var selectedNames = item.SelectedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            var excludedNames = item.ExcludedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            var addedNames = item.AddedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            // Deserialize and fetch side items details
            List<BasketSideItemDto>? selectedSideItems = null;
            if (!string.IsNullOrEmpty(item.SelectedSideItemsJson))
            {
                try
                {
                    var selectedSides = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(item.SelectedSideItemsJson);
                    if (selectedSides != null && selectedSides.Count > 0)
                    {
                        var sideItemIds = selectedSides.Select(s => s.Id).ToList();
                        var sideItems = await _context.Products
                            .Where(p => sideItemIds.Contains(p.Id))
                            .ToListAsync();

                        selectedSideItems = selectedSides.Select(selectedSide =>
                        {
                            var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                            if (sideItem != null)
                            {
                                return new BasketSideItemDto
                                {
                                    Id = sideItem.Id,
                                    Name = sideItem.Name,
                                    Description = sideItem.Description,
                                    Price = sideItem.BasePrice,
                                    ImageUrl = sideItem.ImageUrl,
                                    Quantity = selectedSide.Quantity,
                                    SubTotal = sideItem.BasePrice * selectedSide.Quantity
                                };
                            }
                            return null;
                        }).Where(s => s != null).ToList()!;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize side items JSON for basket item {BasketItemId}", item.Id);
                }
            }

            // Deserialize ingredient quantities
            Dictionary<Guid, int>? ingredientQuantities = null;
            if (!string.IsNullOrEmpty(item.IngredientQuantitiesJson))
            {
                try
                {
                    ingredientQuantities = JsonSerializer.Deserialize<Dictionary<Guid, int>>(item.IngredientQuantitiesJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize ingredient quantities JSON for basket item {BasketItemId}", item.Id);
                }
            }

            return new BasketItemDto
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = item.Product != null ? item.Product.Name : item.Menu?.Name ?? string.Empty,
                MenuId = item.MenuId,
                ProductDescription = item.Product != null ? item.Product.Description : item.Menu?.Description ?? string.Empty,
                ProductImageUrl = item.Product?.ImageUrl ?? string.Empty,
                ProductVariationId = item.ProductVariationId,
                VariationName = item.ProductVariation?.Name,
                VariationContent = item.ProductVariation?.Descriptions?.ToDictionary(
                    d => d.LanguageCode,
                    d => new BasketItemVariationContentDto(d.Name, d.Description)
                ),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                ItemTotal = item.ItemTotal,
                SpecialInstructions = item.SpecialInstructions,
                SelectedIngredients = item.SelectedIngredients,
                ExcludedIngredients = item.ExcludedIngredients,
                AddedIngredients = item.AddedIngredients,
                IngredientQuantities = ingredientQuantities,
                CustomizationPrice = item.CustomizationPrice,
                SelectedIngredientNames = selectedNames,
                ExcludedIngredientNames = excludedNames,
                AddedIngredientNames = addedNames,
                SelectedSideItems = selectedSideItems,
                ChildItems = item.ChildBasketItems.Select(child => new BasketItemDto
                {
                    Id = child.Id,
                    ProductId = child.ProductId,
                    ProductName = child.Product?.Name,
                    Quantity = child.Quantity,
                    UnitPrice = child.UnitPrice,
                    ItemTotal = child.ItemTotal,
                    CustomizationPrice = child.CustomizationPrice,
                    // Map other properties if needed, but for menu options these are usually minimal
                }).ToList()
            };
        }))).ToList();

        // Filter out child items from the top-level list, as they are now nested under their parents
        // We only want items that do NOT have a parent to be at the top level
        var rootItems = allItems.Where(i => 
            !basket.Items.Any(bi => bi.Id == i.Id && bi.ParentBasketItemId.HasValue)
        ).ToList();

        return new BasketDto
        {
            Id = basket.Id,
            UserId = basket.UserId != Guid.Empty ? basket.UserId : null,
            SessionId = basket.SessionId,
            SubTotal = basket.SubTotal,
            Tax = basket.Tax,
            DeliveryFee = basket.DeliveryFee,
            Discount = basket.Discount,
            CustomerDiscount = customerDiscountAmount,
            CustomerDiscountName = customerDiscountName,
            Total = basket.Total,
            PromoCode = basket.PromoCode,
            TotalItems = basket.Items.Where(i => i.ParentBasketItemId == null).Sum(i => i.Quantity), // Count only root items? Or all? Usually root items (bundles) count as 1
            ExpiresAt = basket.ExpiresAt,
            Notes = basket.Notes,
            Items = rootItems
        };
    }

    private string GetCacheKey(string? sessionId, Guid? userId)
    {
        if (userId.HasValue && userId.Value != Guid.Empty)
            return $"{BASKET_CACHE_KEY_PREFIX}user:{userId}";

        return $"{BASKET_CACHE_KEY_PREFIX}session:{sessionId}";
    }

    private async Task CacheBasketAsync(string cacheKey, BasketDto basket)
    {
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(BASKET_CACHE_EXPIRY_MINUTES)
        };

        var json = JsonSerializer.Serialize(basket);
        await _cache.SetStringAsync(cacheKey, json, options);
    }

    private async Task InvalidateBasketCacheAsync(string? sessionId, Guid? userId)
    {
        var cacheKey = GetCacheKey(sessionId, userId);
        await _cache.RemoveAsync(cacheKey);
    }
}
