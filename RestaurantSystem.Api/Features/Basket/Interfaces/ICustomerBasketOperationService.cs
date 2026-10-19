using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface ICustomerBasketOperationService
{
    Task<BasketDto> AddAsync(string sessionId, AddToBasketDto item, Guid operationId,
        CancellationToken cancellationToken);
    Task<BasketDto> ExecuteAsync(string sessionId, Guid operationId, string payload,
        Func<Task<BasketDto>> apply, CancellationToken cancellationToken);
}
