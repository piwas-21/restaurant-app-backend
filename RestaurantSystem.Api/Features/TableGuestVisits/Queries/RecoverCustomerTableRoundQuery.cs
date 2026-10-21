using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Queries;

public sealed record RecoverCustomerTableRoundQuery(string SessionId, CustomerTableRoundProofDto Proof)
    : IQuery<ApiResponse<CustomerTableRoundResolutionDto>>;

public sealed class RecoverCustomerTableRoundQueryHandler(ICustomerTableRoundService service)
    : IQueryHandler<RecoverCustomerTableRoundQuery, ApiResponse<CustomerTableRoundResolutionDto>>
{
    public async Task<ApiResponse<CustomerTableRoundResolutionDto>> Handle(RecoverCustomerTableRoundQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTableRoundResolutionDto>.SuccessWithData(
            await service.ResolveAsync(query.SessionId, query.Proof, false, cancellationToken));
}
