using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries;

public sealed record GetCustomerTablePaymentAccountQuery(string SessionId, string? ParticipantToken,
    int? SchemaVersion, Guid TenantId, Guid ServiceSessionId) : IQuery<ApiResponse<CustomerTablePaymentAccountDto>>;

public sealed class GetCustomerTablePaymentAccountQueryHandler(ICustomerTablePaymentAccountReader reader)
    : IQueryHandler<GetCustomerTablePaymentAccountQuery, ApiResponse<CustomerTablePaymentAccountDto>>
{
    public async Task<ApiResponse<CustomerTablePaymentAccountDto>> Handle(GetCustomerTablePaymentAccountQuery query,
        CancellationToken cancellationToken) => ApiResponse<CustomerTablePaymentAccountDto>.SuccessWithData(
            await reader.ReadAsync(query.SessionId, query.ParticipantToken, query.SchemaVersion, query.TenantId,
                query.ServiceSessionId, cancellationToken));
}
