using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IBasketRepository"/>. A faithful extraction of the basket
/// load / get-or-create / totals-persistence helpers that previously lived inline
/// in <c>BasketService</c>; query shapes and filter semantics are unchanged.
/// </summary>
public class BasketRepository : IBasketRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IBasketPricingService _basketPricingService;

    public BasketRepository(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IBasketPricingService basketPricingService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _basketPricingService = basketPricingService;
    }

    public async Task<Domain.Entities.Basket?> FindBasketAsync(string? sessionId, Guid? userId)
    {
        var query = _context.Baskets
            .AsNoTracking() // Ensure fresh data without EF Core tracking interference
            .AsSplitQuery() // Prevent cartesian explosion with multiple Includes
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.ProductVariation)
                    .ThenInclude(pv => pv!.Descriptions)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Menu)
                .ThenInclude(b => b!.MenuItems)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.ChildBasketItems)
                    .ThenInclude(c => c.Product)
            .Include(b => b.Items)
            .Where(b => !b.IsDeleted);

        var owned = ApplyOwnerFilter(query, sessionId, userId);
        return owned == null ? null : await owned.FirstOrDefaultAsync();
    }

    public async Task<Domain.Entities.Basket> GetOrCreateBasketAsync(string? sessionId, Guid? userId)
    {
        var basket = await FindBasketAsync(sessionId, userId);

        if (basket == null)
        {
            basket = new Domain.Entities.Basket
            {
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.Baskets.Add(basket);
            await _context.SaveChangesAsync();
        }

        return basket;
    }

    public async Task<Domain.Entities.Basket?> FindTrackedBasketWithItemsAsync(string? sessionId, Guid? userId)
    {
        // Tracked (no AsNoTracking) and only the .Items navigation: callers mutate
        // scalar fields and delete child rows, which must persist on SaveChanges, and
        // the heavier product/variation/menu includes used by FindBasketAsync are not
        // needed. Filter semantics mirror FindBasketAsync.
        IQueryable<Domain.Entities.Basket> query = _context.Baskets
            .Include(b => b.Items)
            .Where(b => !b.IsDeleted);

        var owned = ApplyOwnerFilter(query, sessionId, userId);
        return owned == null ? null : await owned.FirstOrDefaultAsync();
    }

    public async Task RecalculateTotalsAsync(Guid basketId)
    {
        var basket = await _context.Baskets
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == basketId && !b.IsDeleted);

        if (basket == null)
            return;

        // Pricing (sub-total, customer discount, tax, total) is computed by the
        // dedicated BasketPricingService; persistence stays here.
        await _basketPricingService.ApplyTotalsAsync(basket);

        basket.UpdatedAt = DateTime.UtcNow;
        basket.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();
    }

    // Logged-in users are matched by UserId only; anonymous users by SessionId with no
    // user. Returns null when neither identifier is usable (caller treats as "no basket").
    private static IQueryable<Domain.Entities.Basket>? ApplyOwnerFilter(
        IQueryable<Domain.Entities.Basket> query, string? sessionId, Guid? userId)
    {
        if (userId.HasValue && userId.Value != Guid.Empty)
            return query.Where(b => b.UserId == userId.Value);

        if (!string.IsNullOrEmpty(sessionId))
            return query.Where(b => b.SessionId == sessionId && (b.UserId == null || b.UserId == Guid.Empty));

        return null;
    }
}
