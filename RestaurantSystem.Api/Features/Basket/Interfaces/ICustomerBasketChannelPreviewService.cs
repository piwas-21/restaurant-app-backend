using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface ICustomerBasketChannelPreviewService
{
    Task<CustomerBasketChannelPreviewDto> PreviewAsync(string sessionId, OrderType orderType,
        CancellationToken cancellationToken);
}
