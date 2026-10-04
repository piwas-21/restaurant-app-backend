using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderAmendmentResolutionPolicy
{
    DateTime UtcNow { get; }
    TimeSpan QuoteLifetime { get; }
    bool IsProviderRetrySafe(DateTime requestedAt);
    string? ResolveCurrency(Order source);
    void WarnProviderFailure(Exception failure, Guid legId);
}
