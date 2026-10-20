using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverOriginalOrderCancellationQuery;

public sealed record RecoverOriginalOrderCancellationQuery(string SessionId, CustomerOrderCancellationGuestDto Request) : IQuery<ApiResponse<CustomerOrderCancellationResolutionDto>>;

public sealed class RecoverOriginalOrderCancellationQueryHandler(ICustomerOrderCancellationService service)
    : IQueryHandler<RecoverOriginalOrderCancellationQuery, ApiResponse<CustomerOrderCancellationResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationResolutionDto>> Handle(RecoverOriginalOrderCancellationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationResolutionDto>.SuccessWithData(await service.RecoverOriginalAsync(query.SessionId, query.Request, cancellationToken));
}
