using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>
/// The minimal order state a GUEST may poll after placing an order (order confirmation flows):
/// identity, lifecycle status and the promised ready time. Everything else an <c>OrderDto</c>
/// carries — name, email, phone, address, payments — stays behind authentication; this record is
/// reachable anonymously and must never grow a field a stranger could profit from.
/// </summary>
public sealed record GuestOrderStatusDto(
    string OrderNumber,
    OrderType Type,
    OrderStatus Status,
    DateTime? EstimatedDeliveryTime);
