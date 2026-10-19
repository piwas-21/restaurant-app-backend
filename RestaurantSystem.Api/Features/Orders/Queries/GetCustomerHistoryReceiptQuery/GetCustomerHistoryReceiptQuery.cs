using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerHistoryReceiptQuery;

public sealed record GetCustomerHistoryReceiptQuery(int? SchemaVersion, Guid OrderId)
    : IQuery<ApiResponse<CustomerOrderReceiptDto>>;

public sealed class GetCustomerHistoryReceiptQueryHandler(ICustomerOrderReceiptReader reader)
    : IQueryHandler<GetCustomerHistoryReceiptQuery, ApiResponse<CustomerOrderReceiptDto>>
{
    public async Task<ApiResponse<CustomerOrderReceiptDto>> Handle(GetCustomerHistoryReceiptQuery query,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderReceiptDto>.SuccessWithData(
            await reader.ReadCustomerAsync(query.OrderId, cancellationToken));
}
