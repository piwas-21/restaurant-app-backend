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
            // Remove the parent lines; bundle children cascade via the Items→ParentBasketItem
            // relationship, so they must not be deleted individually here.
            var doomed = basket.Items.Where(i => doomedIds.Contains(i.Id)).ToList();
            _context.BasketItems.RemoveRange(doomed);
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
    /// Top-level lines the requested order type forbids. Bundle CHILD lines are skipped on purpose:
    /// a bundle carries its own channel set (no auto-intersection with its children), so the parent
    /// line is the only thing whose availability governs the order.
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
