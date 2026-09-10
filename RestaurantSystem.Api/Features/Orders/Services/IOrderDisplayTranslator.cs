using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Translates loaded, no-tracking order display names for a requested printer-feed language.</summary>
public interface IOrderDisplayTranslator
{
    /// <summary>Rewrites translated display names in place; missing translations retain frozen checkout names.</summary>
    void Apply(IEnumerable<Order> orders, string? language);
}
