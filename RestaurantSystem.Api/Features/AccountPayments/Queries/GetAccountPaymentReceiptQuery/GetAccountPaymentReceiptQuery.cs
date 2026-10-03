using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentReceiptQuery;

public sealed record GetAccountPaymentReceiptQuery(Guid AttemptId, string? ReceiptCredential)
    : IQuery<ApiResponse<AccountPaymentReceiptDto>>;

public sealed class GetAccountPaymentReceiptQueryHandler(IAccountPaymentReceiptReader reader)
    : IQueryHandler<GetAccountPaymentReceiptQuery, ApiResponse<AccountPaymentReceiptDto>>
{
    public async Task<ApiResponse<AccountPaymentReceiptDto>> Handle(GetAccountPaymentReceiptQuery query,
        CancellationToken cancellationToken) => ApiResponse<AccountPaymentReceiptDto>.SuccessWithData(
        await reader.ReadAsync(query.AttemptId, query.ReceiptCredential, cancellationToken), "Contribution receipt");
}
