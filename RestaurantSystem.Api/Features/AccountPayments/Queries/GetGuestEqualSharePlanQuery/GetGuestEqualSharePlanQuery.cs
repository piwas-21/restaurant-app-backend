using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestEqualSharePlanQuery;

public sealed record GetGuestEqualSharePlanQuery(
    Guid ServiceSessionId, Guid OperationId, string? ParticipantCredential)
    : IQuery<ApiResponse<AccountEqualSharePlanDto>>;

public sealed class GetGuestEqualSharePlanQueryHandler(IAccountPaymentOperationReader reader)
    : IQueryHandler<GetGuestEqualSharePlanQuery, ApiResponse<AccountEqualSharePlanDto>>
{
    public async Task<ApiResponse<AccountEqualSharePlanDto>> Handle(
        GetGuestEqualSharePlanQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountEqualSharePlanDto>.SuccessWithData(await reader.GetGuestEqualSharePlanAsync(
            query.ServiceSessionId, query.OperationId, query.ParticipantCredential, cancellationToken),
            "Guest equal-share plan retrieved");
}
