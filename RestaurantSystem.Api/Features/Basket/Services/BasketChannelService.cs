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
                Basket = await _mappingService.MapAsync(basket)
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

        var refreshed = await _basketRepository.FindBasketAsync(sessionId, userId);

        return new BasketChannelSwitchDto
        {
            Applied = true,
            Conflicts = [],
            Removed = conflicts,
            Basket = refreshed is null ? null : await _mappingService.MapAsync(refreshed)
        };
    }

    /// <summary>
    /// Top-level lines the requested order type forbids.
    /// </summary>
    /// <remarks>
    /// Bundle CHILD lines are skipped here by design — the intent is that a bundle carries its own
    /// channel set. ⚠️ That premise is NOT yet implemented: no bundle command accepts
    /// <c>AvailableOrderTypes</c>, so a bundle's mask is always inherited-or-null today and nothing
    /// constrains it. <c>OrderChannelGuard</c> does walk children at order creation, so an
    /// unfulfillable ORDER cannot be created; the residual gap is that such a line can still be
    /// added to a basket. Tracked as follow-up with bundle mask support.
    /// </summary>
    private async Task<List<BasketChannelConflictDto>> FindConflictsAsync(
        Domain.Entities.Basket basket,
        OrderType orderType,
        CancellationToken cancellationToken)
    {
        var productIds = basket.Items
            .Where(i => i.ParentBasketItemId is null && i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value)
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
        foreach (var item in basket.Items.Where(i => i.ParentBasketItemId is null && i.ProductId.HasValue))
        {
            if (!products.TryGetValue(item.ProductId!.Value, out var product))
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
