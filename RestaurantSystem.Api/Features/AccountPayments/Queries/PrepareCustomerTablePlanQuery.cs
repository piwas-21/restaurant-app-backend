using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries;

public sealed record PrepareCustomerTablePlanQuery(string SessionId, string? ParticipantToken, CustomerTablePlanIntentDto Request) : IQuery<ApiResponse<CustomerTablePlanPreparedDto>>;

public sealed class PrepareCustomerTablePlanQueryHandler(ICustomerTablePlans plans) : IQueryHandler<PrepareCustomerTablePlanQuery, ApiResponse<CustomerTablePlanPreparedDto>>
{
    public async Task<ApiResponse<CustomerTablePlanPreparedDto>> Handle(PrepareCustomerTablePlanQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePlanPreparedDto>.SuccessWithData(await plans.PrepareAsync(query.SessionId, query.ParticipantToken, query.Request, cancellationToken));
}
