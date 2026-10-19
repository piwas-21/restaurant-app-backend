using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface ICustomerBasketItemMutationService
{
    Task<BasketDto> MutateAsync(string sessionId, Guid basketItemId, CustomerBasketItemMutationDto request,
        CancellationToken cancellationToken);
}
