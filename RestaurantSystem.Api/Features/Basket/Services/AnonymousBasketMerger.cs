using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IAnonymousBasketMerger"/>. Merges the anonymous (session) basket
/// into the user's basket at login. Audit stamping uses
/// <c>ICurrentUserService.GetAuditIdentifier()</c> per CLAUDE.md §5.13. This merge runs from
/// the on-login event handler, where the request principal is not yet the logging-in user,
/// so the audit identifier resolves to the project-standard "System" for this automatic
/// operation — an audit-string-only detail with no functional impact.
///
/// Baskets are loaded WITH change tracking (<see cref="IBasketRepository.FindTrackedBasketWithItemsAsync"/>)
/// so the adopt (UserId) and soft-delete (IsDeleted) mutations actually persist — the prior
/// AsNoTracking load silently dropped them (PR #89 review). The result is mapped from a fresh
/// full-graph <see cref="IBasketRepository.FindBasketAsync"/> load so the returned DTO carries
/// the product/variation/menu graph the tracked Items-only load omits.
///
/// When a duplicate's quantity is merged into an existing user item, the now-redundant anonymous
/// row is hard-deleted — but ONLY for standalone leaf items. BasketItem is not soft-delete-aware,
/// so a Remove is a real DELETE, and the self-referencing parent/child FK is Restrict; deleting a
/// menu-bundle parent (or a child whose parent is being moved) would break that FK. Bundle
/// duplicates are therefore left under the soft-deleted anonymous basket (invisible, not
/// double-counted) pending the bundle-aware merge redesign tracked separately.
/// </summary>
public class AnonymousBasketMerger : IAnonymousBasketMerger
{
    private readonly ApplicationDbContext _context;
    private readonly IBasketRepository _basketRepository;
    private readonly IBasketMappingService _basketMappingService;
    private readonly ICurrentUserService _currentUserService;

    public AnonymousBasketMerger(
        ApplicationDbContext context,
        IBasketRepository basketRepository,
        IBasketMappingService basketMappingService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _basketRepository = basketRepository;
        _basketMappingService = basketMappingService;
        _currentUserService = currentUserService;
    }

    public async Task<BasketDto> MergeAsync(string sessionId, Guid userId)
    {
        // Tracked loads: the header mutations below (UserId, IsDeleted) must persist.
        var anonymousBasket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, null);
        var userBasket = await _basketRepository.FindTrackedBasketWithItemsAsync(null, userId);

        if (anonymousBasket == null)
        {
            // Nothing to merge: return the user's basket (or a fresh one), mapped from the
            // full-graph load so the DTO carries product/variation/menu details.
            return await MapByUserAsync(userId)
                ?? await _basketMappingService.MapAsync(await _basketRepository.GetOrCreateBasketAsync(sessionId, userId));
        }

        if (userBasket == null)
        {
            // Adopt the anonymous basket as the user's. Tracked, so this persists.
            anonymousBasket.UserId = userId;
            anonymousBasket.UpdatedAt = DateTime.UtcNow;
            anonymousBasket.UpdatedBy = _currentUserService.GetAuditIdentifier();
            await _context.SaveChangesAsync();

            return await MapByUserAsync(userId)
                ?? throw new BadRequestException("Failed to retrieve basket");
        }

        // Merge anonymous items into the user basket. Both item sets are already loaded
        // (tracked) by the calls above, so the matching is in-memory — no per-item query.
        // Snapshot first: removing a merged duplicate (below) mutates anonymousBasket.Items
        // via EF relationship fix-up, which would otherwise break the enumeration.
        var anonymousItems = anonymousBasket.Items.ToList();

        // Items that are menu-bundle parents within the anonymous basket (some other item
        // points at them). Used to keep the duplicate-removal below to safe standalone leaves.
        var parentItemIds = anonymousItems
            .Where(i => i.ParentBasketItemId.HasValue)
            .Select(i => i.ParentBasketItemId!.Value)
            .ToHashSet();

        foreach (var item in anonymousItems)
        {
            var existingItem = userBasket.Items.FirstOrDefault(bi =>
                bi.ProductId == item.ProductId &&
                bi.ProductVariationId == item.ProductVariationId);

            if (existingItem != null)
            {
                existingItem.Quantity += item.Quantity;
                existingItem.ItemTotal = existingItem.Quantity * existingItem.UnitPrice;
                existingItem.UpdatedAt = DateTime.UtcNow;
                existingItem.UpdatedBy = _currentUserService.GetAuditIdentifier();

                // The anonymous duplicate is now redundant. Hard-delete it ONLY when it is a
                // standalone leaf (neither a bundle parent nor a child) — see the class summary
                // for why bundle-entangled rows are left for the soft-deleted basket to carry.
                bool isStandaloneLeaf = item.ParentBasketItemId == null && !parentItemIds.Contains(item.Id);
                if (isStandaloneLeaf)
                {
                    _context.BasketItems.Remove(item);
                }
            }
            else
            {
                item.BasketId = userBasket.Id;
                item.UpdatedAt = DateTime.UtcNow;
                item.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }
        }

        // Soft-delete the anonymous basket. Tracked, so this persists.
        anonymousBasket.IsDeleted = true;
        anonymousBasket.DeletedAt = DateTime.UtcNow;
        anonymousBasket.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
        await _basketRepository.RecalculateTotalsAsync(userBasket.Id);

        return await MapByUserAsync(userId)
            ?? throw new BadRequestException("Failed to retrieve basket");
    }

    // Re-loads the user's basket with the full item graph (FindBasketAsync) and maps it.
    private async Task<BasketDto?> MapByUserAsync(Guid userId)
    {
        var basket = await _basketRepository.FindBasketAsync(null, userId);
        return basket != null ? await _basketMappingService.MapAsync(basket) : null;
    }
}
