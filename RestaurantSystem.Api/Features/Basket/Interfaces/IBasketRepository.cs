namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Encapsulates all <c>ApplicationDbContext</c> access for baskets — loading,
/// get-or-create, and persisting recalculated totals. Extracted from
/// <c>BasketService</c> (Sprint 3 god-class decomposition) so the service
/// orchestrates rather than queries. Lifetime is scoped, so it shares the same
/// per-request <c>ApplicationDbContext</c> instance as its callers — entities it
/// tracks are visible to mutations performed elsewhere in the same request.
/// </summary>
public interface IBasketRepository
{
    /// <summary>
    /// Loads a basket (AsNoTracking, split query) with the full item graph eagerly
    /// loaded for mapping. Logged-in users match by UserId; anonymous users by
    /// SessionId with no user. Returns null when neither identifier is usable or
    /// no basket exists.
    /// </summary>
    Task<Domain.Entities.Basket?> FindBasketAsync(string? sessionId, Guid? userId);

    /// <summary>
    /// Loads the basket as <see cref="FindBasketAsync"/>, creating and persisting an
    /// empty one (7-day expiry) when none exists.
    /// </summary>
    Task<Domain.Entities.Basket> GetOrCreateBasketAsync(string? sessionId, Guid? userId);

    /// <summary>
    /// Loads a basket WITH change tracking and only its <c>Items</c> navigation — for
    /// mutation paths (e.g. clear) where scalar edits and child-row deletes must persist
    /// and the heavier product/variation/menu includes are not needed.
    /// </summary>
    Task<Domain.Entities.Basket?> FindTrackedBasketWithItemsAsync(string? sessionId, Guid? userId);

    /// <summary>
    /// Reloads the basket with its items, recomputes the monetary totals via
    /// <see cref="IBasketPricingService"/>, stamps the audit fields, and saves.
    /// No-op if the basket no longer exists.
    /// </summary>
    Task RecalculateTotalsAsync(Guid basketId);
}
