using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetReleasedTableServiceSessionsQuery;

public sealed record GetReleasedTableServiceSessionsQuery : IQuery<ApiResponse<List<TableServiceSessionDto>>>;

public sealed class GetReleasedTableServiceSessionsQueryHandler
    : IQueryHandler<GetReleasedTableServiceSessionsQuery, ApiResponse<List<TableServiceSessionDto>>>
{
    private readonly ITableServiceSessionReader _reader;

    public GetReleasedTableServiceSessionsQueryHandler(ITableServiceSessionReader reader) => _reader = reader;

    public async Task<ApiResponse<List<TableServiceSessionDto>>> Handle(
        GetReleasedTableServiceSessionsQuery query, CancellationToken cancellationToken)
    {
        var sessions = await _reader.ReadReleasedAsync(cancellationToken);
        return ApiResponse<List<TableServiceSessionDto>>.SuccessWithData(
            sessions.ToList(), "Released table visits retrieved");
    }
}
