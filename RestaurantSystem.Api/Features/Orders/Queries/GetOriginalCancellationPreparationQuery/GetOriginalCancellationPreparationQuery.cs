using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOriginalCancellationPreparationQuery;

public sealed record GetOriginalCancellationPreparationQuery(string SessionId, CustomerOrderRecoveryDto Proof) : IQuery<ApiResponse<CustomerOrderCancellationPreparationDto>>;

public sealed class GetOriginalCancellationPreparationQueryHandler(ICustomerOrderCancellationService service)
    : IQueryHandler<GetOriginalCancellationPreparationQuery, ApiResponse<CustomerOrderCancellationPreparationDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationPreparationDto>> Handle(GetOriginalCancellationPreparationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationPreparationDto>.SuccessWithData(await service.PrepareOriginalAsync(query.SessionId, query.Proof, cancellationToken));
}
