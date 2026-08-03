using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Owns the basket's order type (channel): setting it, and reconciling lines the new channel
/// forbids. Separate from <c>BasketService</c>, which is already over its 300-LOC service budget.
/// </summary>
public class BasketChannelService : IBasketChannelService
{
    private readonly ApplicationDbContext _context;
    private readonly IBasketRepository _basketRepository;
    private readonly IBasketMappingService _mappingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<BasketChannelService> _logger;

    public BasketChannelService(
        ApplicationDbContext context,
        IBasketRepository basketRepository,
        IBasketMappingService mappingService,
        ICurrentUserService currentUserService,
        ILogger<BasketChannelService> logger)
    {
        _context = context;
        _basketRepository = basketRepository;
        _mappingService = mappingService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<BasketChannelSwitchDto> SetOrderTypeAsync(
        string sessionId,
        Guid? userId,
        OrderType orderType,
        bool removeConflicts,
        CancellationToken cancellationToken = default)
    {
        // Both identifiers empty addresses no basket at all. Without this the upsert below would
        // happily CREATE one under a random session id that no later request can ever name — an
        // orphan row, reported back as Applied = true with a null Basket. The NotFoundException this
        // method used to throw was carrying that guard for free.
        if (string.IsNullOrEmpty(sessionId) && (userId is null || userId == Guid.Empty))
        {
            throw new BadRequestException("Session ID or an authenticated user is required");
        }

        // §9.13: UPSERT rather than 404. This endpoint used to refuse an empty cart by
        // construction — only the add path ever created a basket — so a guest who picked a channel
        // before adding anything had the choice silently dropped, and the client had no way to tell
        // (nothing on the wire carried the basket's channel until BasketDto.OrderType).
        //
        // ⚠️ The re-fetch is NOT redundant. GetOrCreateBasketAsync returns a TRACKED entity on its
        // create path but an UNTRACKED one on its find path (FindBasketAsync is AsNoTracking), so
        // taking its return value directly is only correct if it definitely created. Filter parity
        // between the two finders makes that true at an instant but NOT across two round trips: a
        // concurrent add-to-basket, a double-tap or a client retry can insert the row in between,
        // GetOrCreate then FINDS it, and the OrderType assignment below lands on a detached entity —
        // saving nothing, throwing nothing, and answering Applied = true. That is the PR #89 class,
        // which is exactly what this comment used to argue was impossible. Re-reading through the
        // tracked finder is correct on every interleaving and costs one cheap query on create only.
        var basket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, userId);
        if (basket is null)
        {
            await _basketRepository.GetOrCreateBasketAsync(sessionId, userId);
            basket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, userId)
                ?? throw new NotFoundException("Basket not found", ErrorCodes.BasketNotFound);
        }

        var conflicts = await FindConflictsAsync(basket, orderType, cancellationToken);

        // Phase one: report and change NOTHING, so the client can show an itemized confirm before
        // anything is destroyed. Silently dropping lines here would be the worst outcome.
        if (conflicts.Count > 0 && !removeConflicts)
        {
            return new BasketChannelSwitchDto
            {
                Applied = false,
                Conflicts = conflicts,
                Basket = await MapFullGraphAsync(sessionId, userId)
            };
        }

        if (conflicts.Count > 0)
        {
            var doomedIds = conflicts.Select(c => c.BasketItemId).ToHashSet();

            // Children MUST be removed explicitly, before their parents. The self-referencing
            // Items→ParentBasketItem FK has NO cascade rule (ParentBasketItemId is nullable, so EF
            // uses ClientSetNull and the DB rule is NO ACTION). Removing only the parents would set
            // each tracked child's ParentBasketItemId to null instead of deleting it — promoting
            // bundle children to top-level basket lines, inflating the item count, and putting them
            // on the kitchen ticket. Mirrors BasketService.RemoveItemFromBasketAsync.
            var doomedChildren = basket.Items
                .Where(i => i.ParentBasketItemId.HasValue && doomedIds.Contains(i.ParentBasketItemId.Value))
                .ToList();
            var doomedParents = basket.Items.Where(i => doomedIds.Contains(i.Id)).ToList();

            _context.BasketItems.RemoveRange(doomedChildren);
            _context.BasketItems.RemoveRange(doomedParents);
        }

        basket.OrderType = orderType;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);
        await _basketRepository.RecalculateTotalsAsync(basket.Id);

        _logger.LogInformation(
            "Basket {BasketId} order type set to {OrderType}; {RemovedCount} conflicting line(s) removed",
            basket.Id, orderType, conflicts.Count);

        return new BasketChannelSwitchDto
        {
            Applied = true,
            Conflicts = [],
            Removed = conflicts,
            Basket = await MapFullGraphAsync(sessionId, userId)
        };
    }

    public async Task<BasketDto?> ClearOrderTypeAsync(
        string sessionId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        // Same guard as the set path, for the same reason: with neither identifier there is no
        // basket to address, and answering "cleared" for a basket nobody named is a lie.
        if (string.IsNullOrEmpty(sessionId) && (userId is null || userId == Guid.Empty))
        {
            throw new BadRequestException("Session ID or an authenticated user is required");
        }

        // No GetOrCreate here — see the interface remarks. Nothing to clear is a SUCCESS, not a 404:
        // the caller asked for "this basket has no channel", and a basket that does not exist
        // already satisfies that. Making it a 404 would push every client into treating a normal
        // outcome as an error, which is how §9.13's blindness started.
        var basket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, userId);
        if (basket is null)
        {
            _logger.LogInformation("No basket to clear the order type on for session {SessionId}", sessionId);
            return null;
        }

        // Idempotent by construction, but skip the write when there is nothing to change so a repeat
        // call does not churn UpdatedAt/UpdatedBy and make an audit trail look like real activity.
        if (basket.OrderType is null)
        {
            return await MapFullGraphAsync(sessionId, userId);
        }

        var previous = basket.OrderType;
        basket.OrderType = null;
        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        // Totals are deliberately NOT recalculated. RecalculateTotalsAsync sums LINE totals, and
        // clearing removes no lines — unlike the set path, which calls it because it may have
        // deleted some. Tax and delivery are resolved per order type at checkout, not stored here.
        _logger.LogInformation(
            "Basket {BasketId} order type cleared (was {PreviousOrderType})", basket.Id, previous);

        return await MapFullGraphAsync(sessionId, userId);
    }

    /// <summary>
    /// Re-reads the basket through the FULL-graph load and maps it. Both exits use this; neither may
    /// map the <c>basket</c> local.
    /// </summary>
    /// <remarks>
    /// That local comes from <see cref="IBasketRepository.FindTrackedBasketWithItemsAsync"/>, which
    /// includes <c>.Items</c> and nothing else. <c>BasketMappingService</c> reads product /
    /// variation / menu through null-conditionals, so mapping that graph THROWS NOTHING and simply
    /// emits <c>ProductName = ""</c> (<c>null</c> on a bundle CHILD line, which has no
    /// <c>?? string.Empty</c> fallback), <c>ProductDescription = ""</c>,
    /// <c>ProductImageUrl = ""</c>, and — on the lines that carry them — a null variation name and
    /// raw GUID strings where the ingredient names belong. The conflict list itself is unaffected:
    /// it names products from its own query. So the damage lands on the basket the client
    /// re-renders from, and only on the blocked branch, which is why the success path's re-read hid
    /// it.
    /// <para>
    /// The invariant that keeps the narrow load safe is <b>no caller may MAP that graph while it
    /// still has lines</b> — not "callers only mutate". <c>BasketService.ClearBasketAsync</c> does
    /// map it, and is correct only because it empties <c>Items</c> first.
    /// </para>
    /// </remarks>
    private async Task<BasketDto?> MapFullGraphAsync(string sessionId, Guid? userId)
    {
        var full = await _basketRepository.FindBasketAsync(sessionId, userId);
        return full is null ? null : await _mappingService.MapAsync(full);
    }

    /// <summary>
    /// Top-level lines the requested order type forbids — judged on the WHOLE line, including its
    /// bundle children and side items.
    /// </summary>
    /// <remarks>
    /// §9.15: this scan used to walk root rows only, which left §9.3's fix an add-then-switch twin.
    /// The ordinary guest journey defeated it: browse with NO channel chosen (permissive by design,
    /// and the dominant browse state) → add a combo whose component is takeaway-only → switch to
    /// Dine-in → zero conflicts, no confirm dialog, channel set → <c>OrderChannelGuard</c> flattens
    /// children at checkout and returns 400. The guest chose, was told nothing, and was refused at
    /// the till.
    /// <para>
    /// It now agrees with <c>OrderChannelGuard</c> by scanning the same set — see
    /// <see cref="BasketLineChannelScan"/>, which also owns the <c>ProductCategories → Category</c>
    /// include that keeps the verdict meaningful.
    /// </para>
    /// <para>
    /// Conflicts stay ROOT-granular even when the blocked part is a component: the reported
    /// <c>AllowedOrderTypes</c> is the intersection across the line, which is what
    /// <see cref="BasketChannelConflictDto.AllowedOrderTypes"/> already promises ("order types this
    /// line IS available on"). Naming the component instead would need a DTO field and a client
    /// change; naming the line is both true and actionable, since the line is what gets removed.
    /// </para>
    /// </remarks>
    private async Task<List<BasketChannelConflictDto>> FindConflictsAsync(
        Domain.Entities.Basket basket,
        OrderType orderType,
        CancellationToken cancellationToken)
    {
        var allItems = basket.Items.ToList();
        var roots = allItems.Where(i => i.ParentBasketItemId is null).ToList();

        var productIds = BasketLineChannelScan.CollectProductIds(roots, allItems);
        if (productIds.Count == 0)
        {
            return [];
        }

        var products = await BasketLineChannelScan.LoadProductsAsync(_context, productIds, cancellationToken);

        var conflicts = new List<BasketChannelConflictDto>();
        foreach (var item in roots)
        {
            // Judge the line BEFORE resolving its name. An unresolvable root — its product
            // soft-deleted out from under the basket, which `DeleteProductCommand` does without
            // touching basket rows — must not discard its children unscanned: `OrderChannelGuard`
            // would still resolve those children at checkout and refuse the order, which is the
            // §9.15 symptom this method exists to prevent.
            var mask = BasketLineChannelScan.LineMask(item, allItems, products);
            if (OrderChannelMap.Allows(mask, orderType))
            {
                continue;
            }

            var product = item.ProductId is { } productId && products.TryGetValue(productId, out var resolved)
                ? resolved
                : null;

            conflicts.Add(new BasketChannelConflictDto
            {
                BasketItemId = item.Id,
                ProductId = product?.Id ?? item.ProductId,
                // Empty rather than a placeholder, because that is already what every other surface
                // shows for this line: `BasketMappingService` reads the product null-conditionally,
                // so a deleted-product line renders nameless in the cart too. Inventing a name here
                // would need a locale key for a state the rest of the app leaves blank.
                ProductName = product?.Name ?? string.Empty,
                Quantity = item.Quantity,
                AllowedOrderTypes = OrderChannelMap.ToOrderTypes(mask)
            });
        }

        return conflicts;
    }
}
