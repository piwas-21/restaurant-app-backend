using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Resolves the currency an order DISPLAYS its money in (POS plan C18): the first
/// payment tender that carries a currency wins — the money actually moved in that
/// currency — otherwise the tenant's declared <see cref="RestaurantInfo.Currency"/>.
/// Otherwise null, and the consumer must not invent a label.
/// </summary>
public interface IOrderDisplayCurrencyResolver
{
    /// <summary>Null when neither a tender nor the tenant declares a currency.</summary>
    string? Resolve(Order order);
}
