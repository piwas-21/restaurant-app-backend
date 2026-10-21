using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Queries;

public sealed record PrepareCustomerTableRoundQuery(string SessionId, string? ParticipantToken, CustomerTableRoundIntentDto Request)
    : IQuery<ApiResponse<CustomerTableRoundPreparedDto>>;

public sealed class PrepareCustomerTableRoundQueryHandler(ICustomerTableRoundService service)
    : IQueryHandler<PrepareCustomerTableRoundQuery, ApiResponse<CustomerTableRoundPreparedDto>>
{
    public async Task<ApiResponse<CustomerTableRoundPreparedDto>> Handle(PrepareCustomerTableRoundQuery query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTableRoundPreparedDto>.SuccessWithData(
            await service.PrepareAsync(query.SessionId, query.ParticipantToken, query.Request, cancellationToken));
}
