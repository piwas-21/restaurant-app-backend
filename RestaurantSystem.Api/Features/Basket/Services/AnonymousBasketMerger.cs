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
/// Note (out of scope, tracked separately): when a duplicate item's quantity is merged into an
/// existing user item, the now-redundant anonymous row is left under the soft-deleted anonymous
/// basket rather than deleted. It is invisible (the basket is soft-deleted) and not double-counted,
/// so it is harmless cruft; a bundle-aware merge that also prunes it is deferred because naive
/// removal is unsafe against the parent/child BasketItem Restrict FK.
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
        foreach (var item in anonymousBasket.Items)
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
