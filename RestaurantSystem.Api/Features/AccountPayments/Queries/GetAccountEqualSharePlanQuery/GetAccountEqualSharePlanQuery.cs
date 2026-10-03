using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountEqualSharePlanQuery;

public sealed record GetAccountEqualSharePlanQuery(Guid ServiceSessionId, Guid OperationId)
    : IQuery<ApiResponse<AccountEqualSharePlanDto>>;

public sealed class GetAccountEqualSharePlanQueryHandler(IAccountPaymentOperationReader reader)
    : IQueryHandler<GetAccountEqualSharePlanQuery, ApiResponse<AccountEqualSharePlanDto>>
{
    public async Task<ApiResponse<AccountEqualSharePlanDto>> Handle(
        GetAccountEqualSharePlanQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountEqualSharePlanDto>.SuccessWithData(await reader.GetEqualSharePlanAsync(
            query.ServiceSessionId, query.OperationId, cancellationToken), "Equal-share plan retrieved");
}
