using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries;

public sealed record PrepareCustomerTablePaymentQuery(string SessionId, string? ParticipantToken,
    CustomerTablePaymentIntentDto Request) : IQuery<ApiResponse<CustomerTablePaymentPreparedDto>>;

public sealed class PrepareCustomerTablePaymentQueryHandler(ICustomerTablePaymentPreparation preparation)
    : IQueryHandler<PrepareCustomerTablePaymentQuery, ApiResponse<CustomerTablePaymentPreparedDto>>
{
    public async Task<ApiResponse<CustomerTablePaymentPreparedDto>> Handle(PrepareCustomerTablePaymentQuery query,
        CancellationToken cancellationToken) => ApiResponse<CustomerTablePaymentPreparedDto>.SuccessWithData(
            await preparation.PrepareAsync(query.SessionId, query.ParticipantToken, query.Request, cancellationToken));
}
