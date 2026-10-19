using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface ICustomerBasketChannelService
{
    Task<BasketDto> SwitchAsync(string sessionId, CustomerBasketChannelDto request,
        CancellationToken cancellationToken);
}
