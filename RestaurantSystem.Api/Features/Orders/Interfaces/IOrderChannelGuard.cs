using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Interfaces;

/// <summary>
/// A staff warn-and-allow that actually happened: who accepted the order, and which items were not
/// available for its channel. Returned so the caller can persist it on the order
/// (ORDER-TYPE-AVAILABILITY-PLAN §9.6) rather than leaving the only trace in a rotating log.
/// </summary>
/// <remarks>
/// A RESULT rather than something the guard writes itself: the guard runs before the order entity
/// exists — it judges the incoming item DTOs — so it has nothing to write to. It also keeps the guard
/// free of persistence concerns, which is why it can stay a per-request service with no knowledge of
/// the order aggregate.
/// </remarks>
public record OrderChannelOverride(string By, string Items);

/// <summary>
/// Enforces per-order-type availability at order creation. Blocks customers, warns-and-allows staff.
/// </summary>
public interface IOrderChannelGuard
{
    /// <summary>
    /// Throws <see cref="Common.Exceptions.BadRequestException"/> when any product -- including bundle children and side items in ChildItems -- is unavailable for
    /// <paramref name="orderType"/> and the caller is not staff.
    /// </summary>
    /// <returns>
    /// The recorded override when a staff member was allowed through, otherwise <c>null</c> — which
    /// is the ordinary case, including every order with nothing blocked.
    /// </returns>
    Task<OrderChannelOverride?> EnsureOrderableAsync(
        IReadOnlyCollection<Dtos.CreateOrderItemDto> items,
        OrderType orderType,
        CancellationToken cancellationToken = default);
}
