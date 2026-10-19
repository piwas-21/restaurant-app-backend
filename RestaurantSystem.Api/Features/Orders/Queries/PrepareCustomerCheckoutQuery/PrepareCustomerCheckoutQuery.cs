using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrepareCustomerCheckoutQuery;

public sealed record PrepareCustomerCheckoutQuery(string SessionId, CustomerCheckoutIntentDto Request)
    : IQuery<ApiResponse<CustomerCheckoutPreparationDto>>;

public sealed class PrepareCustomerCheckoutQueryHandler(ICustomerCheckoutAcceptance service)
    : IQueryHandler<PrepareCustomerCheckoutQuery, ApiResponse<CustomerCheckoutPreparationDto>>
{
    public async Task<ApiResponse<CustomerCheckoutPreparationDto>> Handle(PrepareCustomerCheckoutQuery request,
        CancellationToken cancellationToken) => ApiResponse<CustomerCheckoutPreparationDto>.SuccessWithData(
            await service.PrepareAsync(request.SessionId, request.Request, cancellationToken));
}
