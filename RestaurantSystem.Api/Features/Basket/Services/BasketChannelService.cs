using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
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
        var basket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, userId)
            ?? throw new NotFoundException("Basket not found");

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
    /// Top-level lines the requested order type forbids.
    /// </summary>
    /// <remarks>
    /// Bundle CHILD lines are skipped here by design — the intent is that a bundle carries its own
    /// channel set. ⚠️ That premise is NOT yet implemented: no bundle command accepts
    /// <c>AvailableOrderTypes</c>, so a bundle's mask is always inherited-or-null today and nothing
    /// constrains it. <c>OrderChannelGuard</c> does walk children at order creation, so an
    /// unfulfillable ORDER cannot be created, and since §9.3 such a line can no longer be ADDED
    /// under a chosen channel either (<c>BasketItemFactory</c> guards every option and side item).
    /// <para>
    /// ⚠️ <b>The residual gap is this scan itself.</b> It is root-only, so a combo added while NO
    /// channel was chosen — permissive by design, and the dominant browse state — reports zero
    /// conflicts when the guest later picks one its components refuse. The guest gets no confirm
    /// dialog, the channel is set, and the 400 arrives at checkout instead. Closing it means
    /// flattening children + <c>SelectedSideItemsJson</c> here, which this scan already has the data
    /// for. Tracked as §9.15 (and the same root-only shape sits in
    /// <c>AnonymousBasketMerger.ResetOrderTypeIfMergedItemsConflictAsync</c>).
    /// </remarks>
    private async Task<List<BasketChannelConflictDto>> FindConflictsAsync(
        Domain.Entities.Basket basket,
        OrderType orderType,
        CancellationToken cancellationToken)
    {
        var productIds = basket.Items
            .Where(i => i.ParentBasketItemId is null)
            // OfType filters the nulls AND unwraps in one step. The obvious
            // `.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value)` needs a
            // null-forgiving operator, because C# flow analysis does not carry the Where's
            // guarantee across the Select's lambda boundary.
            .Select(i => i.ProductId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
        {
            return [];
        }

        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var conflicts = new List<BasketChannelConflictDto>();
        foreach (var item in basket.Items.Where(i => i.ParentBasketItemId is null))
        {
            // The `is not { } productId` pattern narrows for the compiler, so no `!` is needed.
            if (item.ProductId is not { } productId || !products.TryGetValue(productId, out var product))
            {
                continue;
            }

            var mask = OrderTypeAvailability.EffectiveMask(product);
            if (OrderChannelMap.Allows(mask, orderType))
            {
                continue;
            }

            conflicts.Add(new BasketChannelConflictDto
            {
                BasketItemId = item.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                AllowedOrderTypes = OrderChannelMap.ToOrderTypes(mask)
            });
        }

        return conflicts;
    }
}
