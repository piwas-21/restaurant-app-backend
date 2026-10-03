using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountPaymentOperationQuery;

public sealed record GetGuestAccountPaymentOperationQuery(
    Guid ServiceSessionId, Guid OperationId, string? ParticipantCredential)
    : IQuery<ApiResponse<AccountPaymentOperationDto>>;

public sealed class GetGuestAccountPaymentOperationQueryHandler(IAccountPaymentOperationReader reader)
    : IQueryHandler<GetGuestAccountPaymentOperationQuery, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        GetGuestAccountPaymentOperationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await reader.GetGuestAttemptAsync(
            query.ServiceSessionId, query.OperationId, query.ParticipantCredential, cancellationToken),
            "Guest payment operation retrieved");
}
