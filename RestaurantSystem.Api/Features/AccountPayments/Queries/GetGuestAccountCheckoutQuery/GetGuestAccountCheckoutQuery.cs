using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountCheckoutQuery;

public sealed record GetGuestAccountCheckoutQuery(Guid ServiceSessionId, Guid OperationId,
    string? ParticipantCredential) : IQuery<ApiResponse<AccountCheckoutStartDto>>;

public sealed class GetGuestAccountCheckoutQueryHandler(IAccountGuestCheckoutReader reader)
    : IQueryHandler<GetGuestAccountCheckoutQuery, ApiResponse<AccountCheckoutStartDto>>
{
    public async Task<ApiResponse<AccountCheckoutStartDto>> Handle(GetGuestAccountCheckoutQuery query,
        CancellationToken cancellationToken) => ApiResponse<AccountCheckoutStartDto>.SuccessWithData(
        await reader.ReadAsync(query.ServiceSessionId, query.OperationId,
            query.ParticipantCredential, cancellationToken), "Checkout status");
}
