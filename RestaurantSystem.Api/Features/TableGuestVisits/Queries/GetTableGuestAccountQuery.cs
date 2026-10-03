using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Queries;

public sealed record GetTableGuestAccountQuery(Guid ServiceSessionId, string? ParticipantToken)
    : IQuery<ApiResponse<TableGuestAccountDto>>;

public sealed class GetTableGuestAccountQueryHandler
    : IQueryHandler<GetTableGuestAccountQuery, ApiResponse<TableGuestAccountDto>>
{
    private readonly ITableGuestAccountReader _reader;

    public GetTableGuestAccountQueryHandler(ITableGuestAccountReader reader) => _reader = reader;

    public async Task<ApiResponse<TableGuestAccountDto>> Handle(
        GetTableGuestAccountQuery query, CancellationToken cancellationToken) =>
        ApiResponse<TableGuestAccountDto>.SuccessWithData(
            await _reader.ReadAsync(query.ServiceSessionId, query.ParticipantToken, cancellationToken),
            "Table account loaded");
}
