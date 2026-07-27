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
    private readonly IBasketMappingService _basketMappingService;
    private readonly IBasketItemFactory _basketItemFactory;
    private readonly IBasketRepository _basketRepository;
    private readonly IAnonymousBasketMerger _anonymousBasketMerger;

    public BasketService(
       ApplicationDbContext context,
       ICurrentUserService currentUserService,
       IBasketMappingService basketMappingService,
       IBasketItemFactory basketItemFactory,
       IBasketRepository basketRepository,
       IAnonymousBasketMerger anonymousBasketMerger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketMappingService = basketMappingService;
        _basketItemFactory = basketItemFactory;
        _basketRepository = basketRepository;
        _anonymousBasketMerger = anonymousBasketMerger;
    }

    public async Task<BasketDto?> GetBasketAsync(string sessionId, Guid? userId = null)
    {
        // IMPORTANT: Caching disabled to fix race condition where stale basket data
        // could be cached during concurrent add/update operations.
        // The basket query is fast enough (indexed by session/user ID) that
        // caching provides minimal benefit but creates significant consistency issues.

        // Get fresh data from database
        var basket = await _basketRepository.FindBasketAsync(sessionId, userId);
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

        var basket = await _basketRepository.GetOrCreateBasketAsync(sessionId, userId);

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
                // Needed so BasketChannelGuard can resolve availability inherited from the
                // PRIMARY category (ORDER-TYPE-AVAILABILITY-PLAN §4.1).
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                .Include(p => p.MenuDefinition)
                    .ThenInclude(md => md!.Sections)
                        .ThenInclude(s => s.Items)
                            .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive && p.IsAvailable);

            if (product == null)
                throw new NotFoundException("Product not found or unavailable");

            BasketChannelGuard.EnsureOrderable(product, basket.OrderType);

            // Handle Menu Type Product. The menu parent/child graph is built by the
            // factory and added in one go — EF cascades the children from the parent.
            if (product.Type == ProductType.Menu)
            {
                var menuItem = await _basketItemFactory.BuildMenuItemAsync(product, item, basket.Id, basket.OrderType);
                _context.BasketItems.Add(menuItem);

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

            // Find exact match including customizations (instructions, selected/excluded
            // ingredients, per-ingredient quantities, AND top-level side items).
            var exactMatch = existingItem.FirstOrDefault(bi => IsSameCustomization(bi, item));

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
                var basketItem = await _basketItemFactory.BuildRegularItemAsync(product, variation, item, basket.Id, basket.OrderType);
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
        var basket = await _basketRepository.FindBasketAsync(sessionId, userId);

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
        var basket = await _basketRepository.FindBasketAsync(sessionId, userId);

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
        // Load WITH tracking + only the .Items navigation (see
        // IBasketRepository.FindTrackedBasketWithItemsAsync): scalar mutations and
        // child-row deletes below must persist, and the heavier product/menu includes
        // that FindBasketAsync eager-loads would be discarded immediately here.
        var userId = _currentUserService.UserId;
        var basket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, userId);
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
        var basket = await _basketRepository.FindBasketAsync(sessionId, _currentUserService.UserId);
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

    public Task<BasketDto> MergeAnonymousBasketAsync(string sessionId, Guid userId)
        => _anonymousBasketMerger.MergeAsync(sessionId, userId);

    public Task RecalculateBasketTotalsAsync(Guid basketId)
        => _basketRepository.RecalculateTotalsAsync(basketId);

    /// <summary>
    /// Two adds dedup onto one basket line only when their full customization matches: special
    /// instructions, selected/excluded ingredients, per-selected-ingredient quantities, AND
    /// top-level side items. The last two were previously ignored, so two otherwise-identical
    /// lines that differed only by their sides or ingredient quantity silently merged into one
    /// (menu-bundles redesign #155).
    /// </summary>
    private static bool IsSameCustomization(BasketItem existing, AddToBasketDto incoming) =>
        (existing.SpecialInstructions ?? "") == (incoming.SpecialInstructions ?? "")
        && SameGuidSet(existing.SelectedIngredients, incoming.SelectedIngredients)
        && SameGuidSet(existing.ExcludedIngredients, incoming.ExcludedIngredients)
        && SameGuidSet(existing.AddedIngredients, incoming.AddedIngredients)
        && SameSideItems(existing.SelectedSideItemsJson, incoming.SelectedSideItems)
        && SameSelectedQuantities(incoming.SelectedIngredients, existing.IngredientQuantitiesJson, incoming.IngredientQuantities);

    private static bool SameGuidSet(List<Guid>? a, List<Guid>? b)
    {
        var la = a ?? new List<Guid>();
        var lb = b ?? new List<Guid>();
        return la.Count == lb.Count && la.OrderBy(x => x).SequenceEqual(lb.OrderBy(x => x));
    }

    private static bool SameSideItems(string? existingJson, List<SelectedSideItemDto>? incoming)
    {
        // Mirror BuildRegularItemAsync: only positive-quantity sides are persisted.
        var incomingSides = (incoming ?? new List<SelectedSideItemDto>())
            .Where(s => s.Quantity > 0)
            .Select(s => (s.Id, s.Quantity))
            .OrderBy(s => s.Id)
            .ToList();

        var existingSides = new List<(Guid Id, int Quantity)>();
        if (!string.IsNullOrEmpty(existingJson))
        {
            var parsed = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(existingJson);
            if (parsed != null)
            {
                // OfType filters out any null array elements (corrupt/hand-edited JSON) without a
                // nullable-reference warning, so the projection below can't NRE.
                existingSides = parsed.OfType<SelectedSideItemDto>().Select(s => (s.Id, s.Quantity)).OrderBy(s => s.Id).ToList();
            }
        }

        return incomingSides.SequenceEqual(existingSides);
    }

    private static bool SameSelectedQuantities(List<Guid>? selectedIngredients, string? existingJson, Dictionary<Guid, int>? incoming)
    {
        var selected = selectedIngredients ?? new List<Guid>();
        if (selected.Count == 0)
        {
            return true; // no selected ingredients → nothing to compare (the selection sets already matched)
        }

        Dictionary<Guid, int>? existing = string.IsNullOrEmpty(existingJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<Guid, int>>(existingJson);

        // A selected ingredient's effective quantity is its map value when > 0, else 1. This
        // ignores the backfilled 0-entries for deselected ingredients (already captured by the
        // selection-set comparison), so a client that omits defaults still dedups correctly.
        return selected.All(id => EffectiveQuantity(incoming, id) == EffectiveQuantity(existing, id));
    }

    private static int EffectiveQuantity(Dictionary<Guid, int>? quantities, Guid ingredientId) =>
        quantities != null && quantities.TryGetValue(ingredientId, out var q) && q > 0 ? q : 1;
}
