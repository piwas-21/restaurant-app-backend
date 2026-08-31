using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Validation;
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
    /// <summary>
    /// The sentence for a basket row that is not there, used by all four throw sites below.
    /// </summary>
    /// <remarks>
    /// A constant for the TEXT only — it is not the discriminator. Two of the four sites pair it
    /// with <see cref="ErrorCodes.BasketNotFound"/> and two deliberately do not (see the comment at
    /// each), so sharing the string must not be read as sharing the contract. Clients branch on the
    /// code; this literal is free to change or be localised.
    /// </remarks>
    private const string BasketNotFoundMessage = "Basket not found";

    /// <summary>The sentence for an addressed item that is not in an existing basket.</summary>
    private const string BasketItemNotFoundMessage = "Basket item not found";

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IBasketMappingService _basketMappingService;
    private readonly IBasketItemFactory _basketItemFactory;
    private readonly IBasketRepository _basketRepository;
    private readonly IAnonymousBasketMerger _anonymousBasketMerger;
    private readonly ILogger<BasketService> _logger;

    public BasketService(
       ApplicationDbContext context,
       ICurrentUserService currentUserService,
       IBasketMappingService basketMappingService,
       IBasketItemFactory basketItemFactory,
       IBasketRepository basketRepository,
       IAnonymousBasketMerger anonymousBasketMerger,
        ILogger<BasketService> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketMappingService = basketMappingService;
        _basketItemFactory = basketItemFactory;
        _basketRepository = basketRepository;
        _anonymousBasketMerger = anonymousBasketMerger;
        _logger = logger;
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

            // TOP-LEVEL only, and deliberately BEFORE the Menu branch below: a bundle's own chosen
            // options are resolved inside BasketItemFactory and never reach this line, so marking
            // a product as a component removes it from the menu without breaking any bundle that
            // offers it.
            BasketComponentGuard.EnsureNotOrderedAlone(product);

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

            // AFTER the lookup, so an inactive/unknown variation id is still a 404 rather than
            // being re-reported as "pick an option" — `variation` is null in both cases here.
            BasketBaseProductGuard.EnsureVariationChosen(product, variation);

            // Check if item with EXACT same customizations already exists in basket.
            //
            // ROOT ROWS ONLY. Without the ParentBasketItemId filter this matched a menu bundle's
            // CHILD rows too: a child carries the same BasketId, its component's ProductId, and a
            // null ProductVariationId, and IsSameCustomization returns true for the ordinary
            // no-customization case. So ordering a standalone Coke while a bundle containing Coke
            // sat in the basket merged the standalone quantity INTO the bundle's child row — the
            // guest got no line of their own, and `exactMatch.ItemTotal = Quantity * UnitPrice`
            // then broke the children-carry-zero invariant and double-counted into the subtotal.
            //
            // The Menu branch above returns before this code, which protects bundle PARENTS from
            // being merged; it never protected their children. AnonymousBasketMerger has always
            // filtered on `ParentBasketItemId == null` here for the same reason (#305).
            var existingItem = await _context.BasketItems
                // Load-bearing for the line total below (#308). A row that reaches this branch is
                // normally a regular item — the Menu branch returns above — but "normally" is doing
                // real work there: `product.Type` is mutable, so a bundle parent whose product was
                // retyped away from Menu DOES fall through to here, and pricing it as a regular item
                // double-charges its customization. Un-included, children read as empty, not as an
                // error, so the count would silently say "regular" for every row.
                .Include(bi => bi.ChildBasketItems)
                .Where(bi =>
                    bi.BasketId == basket.Id &&
                    bi.ParentBasketItemId == null &&
                    bi.ProductId == item.ProductId &&
                    bi.ProductVariationId == item.ProductVariationId)
                .ToListAsync();

            // Find exact match including customizations (instructions, selected/added
            // ingredients, per-ingredient quantities, top-level side items, AND — for a row that
            // turns out to have children — its bundle composition).
            var exactMatch = existingItem.FirstOrDefault(bi => IsSameCustomization(bi, item));

            if (exactMatch != null)
            {
                // Update quantity of existing item with same customizations
                exactMatch.Quantity += item.Quantity;
                // `Quantity * UnitPrice` here DROPPED the customization (#308): a regular item's
                // UnitPrice excludes it, so re-ordering the same customised dish billed the second
                // one without its extras — measured 25.98 for two 15.98 lines.
                exactMatch.ItemTotal = BasketLineTotal.ForRoot(exactMatch, exactMatch.ChildBasketItems.Count);
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
            throw new NotFoundException(BasketNotFoundMessage, ErrorCodes.BasketNotFound);

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .Include(bi => bi.Product)
                .ThenInclude(product => product!.DetailedIngredients)
            // Load-bearing for the rescale below (#305). Without it ChildBasketItems reads as an
            // EMPTY collection rather than throwing, so a bundle's children would silently keep
            // their add-time count and every test would still pass.
            // ...and load-bearing a second time, for the line total below (#308): the child count is
            // what says whether UnitPrice already contains the customization.
            .Include(bi => bi.ChildBasketItems)
                .ThenInclude(child => child.Product)
                    .ThenInclude(product => product!.DetailedIngredients)
            // ROOT ROWS ONLY, the same invariant the add-path dedup above now enforces. A bundle
            // child is not independently addressable: its quantity is DERIVED from the parent's,
            // and its ItemTotal is 0 so it cannot double-count. Updating one directly broke both —
            // measured before this filter, `PUT` on a child id answered 200, set that row to
            // quantity 7 with ItemTotal 10.50, and moved the subtotal 13.00 -> 23.50, charging the
            // component twice (once inside the parent's UnitPrice, once on its own row).
            //
            // It is also the in-app way to manufacture a child whose count is no longer a multiple
            // of its parent's — the exact state BundleChildQuantityScaler has to refuse to rescale.
            // Closing the producer is what keeps that skip branch a deploy-window concern rather
            // than a permanent one.
            //
            // Answers BasketItemNotFound rather than a new code: from a client's point of view a
            // child id is not an addressable basket item, which is what that code already means.
            .FirstOrDefaultAsync(bi =>
                bi.Id == basketItemId && bi.BasketId == basket.Id && bi.ParentBasketItemId == null);

        if (basketItem == null)
            throw new NotFoundException(BasketItemNotFoundMessage, ErrorCodes.BasketItemNotFound);

        // PUT changes only quantity/instructions, but it is also a write boundary for rows created
        // before SauceMax was server-enforced. Validate the root and each bundle child rather than
        // letting a legacy/crafted row become newly active through a later basket mutation.
        SauceSelectionRule.EnsureWithinMaximum(basketItem);
        foreach (var child in basketItem.ChildBasketItems)
        {
            SauceSelectionRule.EnsureWithinMaximum(child);
        }

        // Captured BEFORE the overwrite: it is the divisor that recovers each child's per-unit
        // count. Read it after assigning update.Quantity and every child rescales by 1.
        var previousQuantity = basketItem.Quantity;

        basketItem.Quantity = update.Quantity;
        // Recomputed AFTER the assignment above — the helper reads the row's current Quantity.
        // `Quantity * UnitPrice` is the bundle rule and was applied to every row (#308), so the
        // stepper dropped a regular item's customization: measured 38.97 against 47.94 on a 1 -> 3
        // change, and it did not even need a change to bite — re-submitting the quantity the line
        // already held rewrote 15.98 to 12.99.
        basketItem.ItemTotal = BasketLineTotal.ForRoot(basketItem, basketItem.ChildBasketItems.Count);

        BundleChildQuantityScaler.Rescale(
            basketItem.ChildBasketItems,
            previousQuantity,
            update.Quantity,
            _currentUserService.GetAuditIdentifier());
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
            throw new NotFoundException(BasketNotFoundMessage, ErrorCodes.BasketNotFound);

        var basketItem = await _context.BasketItems
            .Include(bi => bi.Basket)
            .Include(bi => bi.ChildBasketItems) // Include child items for cascade deletion
                                                // ROOT ROWS ONLY — the fourth member of the family in #308's header, and the same filter
                                                // #310 put on the update path. A child carries its parent's BasketId and its component's
                                                // ProductId, so an id-only lookup accepted it: DELETE on a child removed that component
                                                // while the parent's UnitPrice and CustomizationPrice still included it, so the guest
                                                // kept paying for something that had left the kitchen ticket.
                                                //
                                                // It is also load-bearing for BasketLineTotal: individually deleting a bundle's children
                                                // was the ONLY way to manufacture a parent with CustomizationPrice > 0 and no children,
                                                // which is the single state the child-count rule prices wrongly. Removing this filter
                                                // reopens that.
                                                //
                                                // Answers BasketItemNotFound rather than a new code, matching the update path: to a
                                                // client, a child id is not an addressable basket item.
            .FirstOrDefaultAsync(bi =>
                bi.Id == basketItemId && bi.BasketId == basket.Id && bi.ParentBasketItemId == null);

        if (basketItem == null)
            throw new NotFoundException(BasketItemNotFoundMessage, ErrorCodes.BasketItemNotFound);

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
        // Deliberately UNCODED: `ClearBasketCommandHandler` still has the catch-all this change
        // removed from update/remove, so DELETE /api/Basket answers 200 + success:false and no code
        // could reach a client from here anyway. Tagging it would put a promise in ErrorCodes that
        // the wire does not keep. Left alone on purpose — clearing an already-gone basket ends with
        // the cart empty, which is what the caller asked for, so it is not the #415 failure.
        if (basket == null)
            throw new NotFoundException(BasketNotFoundMessage);

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
        // Deliberately UNCODED: unreachable. The only route here, DELETE /api/Basket/promo-code, is
        // a hard-coded 400 stub in BasketController — this method is never entered.
        if (basket == null)
            throw new NotFoundException(BasketNotFoundMessage);

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
    /// instructions, selected/added ingredients, per-selected-ingredient quantities, AND top-level
    /// side items (menu-bundles redesign #155).
    ///
    /// The rule itself now lives in <see cref="BasketLineCustomization"/>, shared with
    /// <c>AnonymousBasketMerger</c>. It was private here, and the merge did not have it — so logging
    /// in collapsed lines this method exists to keep apart (#313).
    /// </summary>
    private bool IsSameCustomization(BasketItem existing, AddToBasketDto incoming) =>
        BasketLineCustomization.AreSame(
            // ChildBasketItems is loaded by the dedup query above and is load-bearing here, for the
            // same reason it is in BasketLineTotal: un-included, it reads as empty rather than
            // throwing, and every bundle would compare as a customization-free regular line.
            BasketLineCustomization.FromRow(existing, existing.ChildBasketItems.ToList(), (ex, what) => _logger.LogWarning(
                ex, "Failed to deserialize {What} JSON for basket item {BasketItemId}", what, existing.Id)),
            BasketLineCustomization.FromRequest(incoming));
}
