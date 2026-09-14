using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableServiceSessionQuery;

public record GetTableServiceSessionQuery(Guid ServiceSessionId)
    : IQuery<ApiResponse<TableServiceSessionDto>>;

public sealed class GetTableServiceSessionQueryHandler
    : IQueryHandler<GetTableServiceSessionQuery, ApiResponse<TableServiceSessionDto>>
{
    private readonly ITableServiceSessionReader _reader;

    public GetTableServiceSessionQueryHandler(ITableServiceSessionReader reader) => _reader = reader;

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        GetTableServiceSessionQuery query, CancellationToken cancellationToken)
    {
        var result = await _reader.ReadAsync(query.ServiceSessionId, cancellationToken);
        return result is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session retrieved");
    }
}
