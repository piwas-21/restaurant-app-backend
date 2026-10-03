using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentAccountQuery;

public sealed record GetAccountPaymentAccountQuery(Guid ServiceSessionId)
    : IQuery<ApiResponse<AccountPaymentAccountDto>>;

public sealed class GetAccountPaymentAccountQueryHandler(IAccountPaymentAccountReader reader)
    : IQueryHandler<GetAccountPaymentAccountQuery, ApiResponse<AccountPaymentAccountDto>>
{
    public async Task<ApiResponse<AccountPaymentAccountDto>> Handle(
        GetAccountPaymentAccountQuery query, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentAccountDto>.SuccessWithData(
            await reader.GetAsync(query.ServiceSessionId, cancellationToken), "Table account retrieved");
}
