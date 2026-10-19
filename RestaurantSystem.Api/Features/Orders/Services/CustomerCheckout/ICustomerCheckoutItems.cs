using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutItems
{
    Task<IReadOnlyList<CreateOrderItemDto>> PopulateAsync(Order order, BasketDto basket, OrderType type,
        CancellationToken cancellationToken);
}
