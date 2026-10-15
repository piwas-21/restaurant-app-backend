using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CashierOrderArchiveDto(
    DateOnly BusinessDate,
    PagedResult<OrderDto> Orders,
    PagedResult<OrderDto> PriorDayUnresolvedOrders);
