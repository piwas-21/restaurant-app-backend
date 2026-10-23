using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries;

public sealed record RecoverCustomerTablePlanQuery(string SessionId, CustomerTablePlanProofDto Proof) : IQuery<ApiResponse<CustomerTablePlanResolutionDto>>;

public sealed class RecoverCustomerTablePlanQueryHandler(ICustomerTablePlans plans) : IQueryHandler<RecoverCustomerTablePlanQuery, ApiResponse<CustomerTablePlanResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePlanResolutionDto>> Handle(RecoverCustomerTablePlanQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePlanResolutionDto>.SuccessWithData(await plans.ResolveAsync(query.SessionId, query.Proof, false, cancellationToken));
}
