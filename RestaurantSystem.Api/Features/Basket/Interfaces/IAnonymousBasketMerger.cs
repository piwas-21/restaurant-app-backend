using RestaurantSystem.Api.Features.Basket.Dtos;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Merges an anonymous (session-scoped) basket into a user's basket at login.
/// Extracted verbatim from <c>BasketService.MergeAnonymousBasketAsync</c> (Sprint 3
/// god-class decomposition); behaviour is unchanged. Distinct from the higher-level
/// <c>Common.Services.IBasketMergeService</c>, which is the login-flow wrapper that
/// swallows errors so a merge failure never breaks login.
/// </summary>
public interface IAnonymousBasketMerger
{
    /// <summary>
    /// Combines the anonymous basket for <paramref name="sessionId"/> into the basket
    /// owned by <paramref name="userId"/> (summing quantities for matching items),
    /// soft-deletes the anonymous basket, recalculates totals, and returns the merged
    /// basket. Falls back to the user's existing or a freshly created basket when there
    /// is no anonymous basket to merge.
    /// </summary>
    Task<BasketDto> MergeAsync(string sessionId, Guid userId);
}
