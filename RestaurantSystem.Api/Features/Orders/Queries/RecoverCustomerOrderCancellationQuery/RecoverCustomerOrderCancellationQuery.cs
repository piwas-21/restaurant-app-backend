using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderCancellationQuery;

public sealed record RecoverCustomerOrderCancellationQuery(CustomerOrderCancellationIntentDto Intent) : IQuery<ApiResponse<CustomerOrderCancellationResolutionDto>>;

public sealed class RecoverCustomerOrderCancellationQueryHandler(ICustomerOrderCancellationService service)
    : IQueryHandler<RecoverCustomerOrderCancellationQuery, ApiResponse<CustomerOrderCancellationResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationResolutionDto>> Handle(RecoverCustomerOrderCancellationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationResolutionDto>.SuccessWithData(await service.RecoverCustomerAsync(query.Intent, cancellationToken));
}
