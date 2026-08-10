using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Decides what an order's existing checkout session still means, and retires it when it means
/// nothing. Separate from the mint path because it is the one thing that stands between a diner and
/// paying twice, and because it is the same question S7's reconciler asks on a timer: <i>what does
/// Stripe say about this row?</i>
/// </summary>
public interface ICheckoutSessionReuse
{
    /// <summary>
    /// The live session's page if there is one, or null to mint a fresh session.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning null when the order must NOT be given a new session — Checkout
    /// already completed, or the live session disagrees with the order's current price. Null means
    /// "nothing usable here, go ahead"; an exception means "do not mint".
    /// </remarks>
    Task<CheckoutSessionDto?> TryReuseAsync(
        IReadOnlyCollection<OrderCheckoutSession> sessions,
        CheckoutAmount amount,
        CancellationToken cancellationToken);
}
