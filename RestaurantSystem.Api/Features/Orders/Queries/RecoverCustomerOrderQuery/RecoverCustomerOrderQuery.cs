using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderQuery;

public sealed record RecoverCustomerOrderQuery(string SessionId, CustomerOrderRecoveryDto Request)
    : IQuery<ApiResponse<CustomerOrderResolutionDto>>;

public sealed class RecoverCustomerOrderQueryHandler(ICustomerCheckoutAcceptance service)
    : IQueryHandler<RecoverCustomerOrderQuery, ApiResponse<CustomerOrderResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderResolutionDto>> Handle(RecoverCustomerOrderQuery request,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderResolutionDto>.SuccessWithData(
            await service.RecoverAsync(request.SessionId, request.Request, cancellationToken));
}
