using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCancellationPreparationQuery;

public sealed record GetCustomerCancellationPreparationQuery(int? SchemaVersion, Guid OrderId) : IQuery<ApiResponse<CustomerOrderCancellationPreparationDto>>;

public sealed class GetCustomerCancellationPreparationQueryHandler(ICustomerOrderCancellationService service)
    : IQueryHandler<GetCustomerCancellationPreparationQuery, ApiResponse<CustomerOrderCancellationPreparationDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationPreparationDto>> Handle(GetCustomerCancellationPreparationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationPreparationDto>.SuccessWithData(await service.PrepareCustomerAsync(query.OrderId, cancellationToken));
}
