using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableServiceSessionReader
{
    Task<TableServiceSessionDto?> ReadAsync(Guid serviceSessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(CancellationToken cancellationToken);
}
