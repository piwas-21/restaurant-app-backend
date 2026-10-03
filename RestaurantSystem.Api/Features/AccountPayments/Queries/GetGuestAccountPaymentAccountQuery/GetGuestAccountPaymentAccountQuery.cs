using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountPaymentAccountQuery;

public sealed record GetGuestAccountPaymentAccountQuery(Guid ServiceSessionId, string? ParticipantCredential)
    : IQuery<ApiResponse<AccountPaymentAccountDto>>;

public sealed class GetGuestAccountPaymentAccountQueryHandler(IAccountPaymentAccountReader reader)
    : IQueryHandler<GetGuestAccountPaymentAccountQuery, ApiResponse<AccountPaymentAccountDto>>
{
    public async Task<ApiResponse<AccountPaymentAccountDto>> Handle(
        GetGuestAccountPaymentAccountQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentAccountDto>.SuccessWithData(await reader.GetGuestAsync(
            query.ServiceSessionId, query.ParticipantCredential, cancellationToken), "Guest account retrieved");
}
