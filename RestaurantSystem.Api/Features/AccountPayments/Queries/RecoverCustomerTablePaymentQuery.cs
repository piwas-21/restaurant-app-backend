using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries;

public sealed record RecoverCustomerTablePaymentQuery(string SessionId, CustomerTablePaymentProofDto Proof) : IQuery<ApiResponse<CustomerTablePaymentResolutionDto>>;

public sealed class RecoverCustomerTablePaymentQueryHandler(ICustomerTablePaymentOperations service)
    : IQueryHandler<RecoverCustomerTablePaymentQuery, ApiResponse<CustomerTablePaymentResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePaymentResolutionDto>> Handle(RecoverCustomerTablePaymentQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePaymentResolutionDto>.SuccessWithData(await service.ResolveAsync(query.SessionId, query.Proof, false, cancellationToken));
}
