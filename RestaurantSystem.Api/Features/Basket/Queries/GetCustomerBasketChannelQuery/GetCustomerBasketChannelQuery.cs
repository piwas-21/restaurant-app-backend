using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Queries.GetCustomerBasketChannelQuery;

public sealed record GetCustomerBasketChannelQuery(string SessionId, OrderType OrderType)
    : IQuery<ApiResponse<CustomerBasketChannelPreviewDto>>;

public sealed class GetCustomerBasketChannelQueryHandler(ICustomerBasketChannelPreviewService channels)
    : IQueryHandler<GetCustomerBasketChannelQuery, ApiResponse<CustomerBasketChannelPreviewDto>>
{
    public async Task<ApiResponse<CustomerBasketChannelPreviewDto>> Handle(GetCustomerBasketChannelQuery query,
        CancellationToken cancellationToken) =>
        ApiResponse<CustomerBasketChannelPreviewDto>.SuccessWithData(
            await channels.PreviewAsync(query.SessionId, query.OrderType, cancellationToken));
}
