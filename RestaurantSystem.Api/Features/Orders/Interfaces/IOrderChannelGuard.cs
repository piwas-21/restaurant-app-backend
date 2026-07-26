using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Interfaces;

/// <summary>
/// Enforces per-order-type availability at order creation. Blocks customers, warns-and-allows staff.
/// </summary>
public interface IOrderChannelGuard
{
    /// <summary>
    /// Throws <see cref="Common.Exceptions.BadRequestException"/> when any product -- including bundle children and side items in ChildItems -- is unavailable for
    /// <paramref name="orderType"/> and the caller is not staff. Staff overrides are logged.
    /// </summary>
    Task EnsureOrderableAsync(
        IReadOnlyCollection<Dtos.CreateOrderItemDto> items,
        OrderType orderType,
        CancellationToken cancellationToken = default);
}
