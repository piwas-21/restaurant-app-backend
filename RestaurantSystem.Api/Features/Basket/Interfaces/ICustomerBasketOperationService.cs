using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface ICustomerBasketOperationService
{
    Task<BasketDto> AddAsync(string sessionId, AddToBasketDto item, Guid operationId,
        CancellationToken cancellationToken);
    Task<CustomerBasketOperationResolutionDto> CancelAddAsync(string sessionId, AddToBasketDto request,
        CancellationToken cancellationToken);
    Task<CustomerBasketOperationResolutionDto> CancelMutationAsync(string sessionId, Guid basketItemId,
        CustomerBasketItemMutationDto request, CancellationToken cancellationToken);
    Task<CustomerBasketOperationResolutionDto> CancelChannelAsync(string sessionId, CustomerBasketChannelDto request,
        CancellationToken cancellationToken);
    Task<BasketDto> ExecuteAsync(string sessionId, Guid operationId, string payload,
        Func<Task<BasketDto>> apply, CancellationToken cancellationToken);
}
