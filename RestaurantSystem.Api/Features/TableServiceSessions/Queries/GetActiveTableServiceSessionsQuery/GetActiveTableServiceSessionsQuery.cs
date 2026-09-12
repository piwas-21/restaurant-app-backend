using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetActiveTableServiceSessionsQuery;

public record GetActiveTableServiceSessionsQuery : IQuery<ApiResponse<List<TableServiceSessionDto>>>;

public sealed class GetActiveTableServiceSessionsQueryHandler
    : IQueryHandler<GetActiveTableServiceSessionsQuery, ApiResponse<List<TableServiceSessionDto>>>
{
    private readonly ITableServiceSessionReader _reader;

    public GetActiveTableServiceSessionsQueryHandler(ITableServiceSessionReader reader) => _reader = reader;

    public async Task<ApiResponse<List<TableServiceSessionDto>>> Handle(
        GetActiveTableServiceSessionsQuery query, CancellationToken cancellationToken)
    {
        var sessions = await _reader.ReadActiveAsync(cancellationToken);
        return ApiResponse<List<TableServiceSessionDto>>.SuccessWithData(
            sessions.ToList(), "Active table service sessions retrieved");
    }
}
