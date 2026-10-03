using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentOperationQuery;

public sealed record GetAccountPaymentOperationQuery(Guid ServiceSessionId, Guid OperationId)
    : IQuery<ApiResponse<AccountPaymentOperationDto>>;

public sealed class GetAccountPaymentOperationQueryHandler(IAccountPaymentOperationReader reader)
    : IQueryHandler<GetAccountPaymentOperationQuery, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        GetAccountPaymentOperationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await reader.GetAttemptAsync(
            query.ServiceSessionId, query.OperationId, cancellationToken), "Payment operation retrieved");
}
