using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IAnonymousBasketMerger"/>. A faithful extraction of
/// <c>BasketService.MergeAnonymousBasketAsync</c>; load semantics (AsNoTracking for the
/// basket headers, tracked for the items being moved) and the merge logic are unchanged.
/// Audit stamping uses <c>ICurrentUserService.GetAuditIdentifier()</c> per CLAUDE.md §5.13
/// (replacing the original inline <c>userId.ToString()</c>). This merge runs from the
/// on-login event handler, where the request principal is not yet the logging-in user, so
/// the audit identifier is the project-standard "System" for this automatic operation
/// rather than the user id — an audit-string-only change with no functional impact.
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
        var anonymousBasket = await _basketRepository.FindBasketAsync(sessionId, null);
        var userBasket = await _basketRepository.FindBasketAsync(null, userId);

        if (anonymousBasket == null)
        {
            return userBasket != null
                ? await _basketMappingService.MapAsync(userBasket)
                : await _basketMappingService.MapAsync(await _basketRepository.GetOrCreateBasketAsync(sessionId, userId));
        }

        if (userBasket == null)
        {
            // Assign anonymous basket to user
            anonymousBasket.UserId = userId;
            anonymousBasket.UpdatedAt = DateTime.UtcNow;
            anonymousBasket.UpdatedBy = _currentUserService.GetAuditIdentifier();
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
                existingItem.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }
            else
            {
                item.BasketId = userBasket.Id;
                item.UpdatedAt = DateTime.UtcNow;
                item.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }
        }

        // Delete anonymous basket
        anonymousBasket.IsDeleted = true;
        anonymousBasket.DeletedAt = DateTime.UtcNow;
        anonymousBasket.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
        await _basketRepository.RecalculateTotalsAsync(userBasket.Id);

        var merged = await _basketRepository.FindBasketAsync(sessionId, userId);
        return merged != null
            ? await _basketMappingService.MapAsync(merged)
            : throw new BadRequestException("Failed to retrieve basket");
    }
}
