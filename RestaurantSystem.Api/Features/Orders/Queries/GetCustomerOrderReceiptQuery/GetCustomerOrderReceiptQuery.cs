using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderReceiptQuery;

public sealed record GetCustomerOrderReceiptQuery(string SessionId, CustomerOrderRecoveryDto Proof)
    : IQuery<ApiResponse<CustomerOrderReceiptDto>>;

public sealed class GetCustomerOrderReceiptQueryHandler(ICustomerOrderReceiptReader reader)
    : IQueryHandler<GetCustomerOrderReceiptQuery, ApiResponse<CustomerOrderReceiptDto>>
{
    public async Task<ApiResponse<CustomerOrderReceiptDto>> Handle(GetCustomerOrderReceiptQuery query,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderReceiptDto>.SuccessWithData(
            await reader.ReadAsync(query.SessionId, query.Proof, cancellationToken));
}
