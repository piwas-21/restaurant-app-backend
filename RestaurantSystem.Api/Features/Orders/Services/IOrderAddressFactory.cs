using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Builds the <see cref="OrderAddress"/> snapshot stored on a new order
/// from one of three input shapes:
///   1. <c>UseAddressId</c> — copy from a saved <c>UserAddress</c>.
///   2. Inline address details on the DTO — copy verbatim.
///   3. No DTO (or only useless DTO) — fall back to the customer's
///      default address if one exists.
///
/// Returns <c>null</c> when none of the three paths produces an address
/// (e.g. dine-in / pickup orders, or delivery orders for guest customers
/// with no inline details). Callers attach the address to the order
/// (or skip if null).
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.7.
/// </summary>
public interface IOrderAddressFactory
{
    Task<OrderAddress?> CreateAsync(
        CreateOrderDeliveryAddressDto? addressDto,
        Guid orderId,
        Guid? userId,
        CancellationToken cancellationToken);
}
