using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerTasksQuery;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

public interface IServerTaskReader
{
    Task<ServerTaskFeedDto> ReadAsync(
        GetServerTasksQuery query,
        CancellationToken cancellationToken);
}

public interface IServerTaskOrderReader
{
    Task<List<ServerServiceTaskDto>> LoadPageAsync(
        long? upperSequence,
        DateTime serverTime,
        string bucket,
        int pageSize,
        ServerTaskPagePosition? after,
        CancellationToken cancellationToken);

    Task<int> CountAsync(
        long? upperSequence,
        DateTime serverTime,
        string bucket,
        CancellationToken cancellationToken);

    Task<List<ServerServiceTaskDto>> LoadByIdsAsync(
        IReadOnlyCollection<Guid> orderIds,
        DateTime serverTime,
        CancellationToken cancellationToken);

    List<ServerServiceTaskDto> Filter(
        IEnumerable<ServerServiceTaskDto> tasks,
        string bucket);

}

public sealed record ServerTaskPagePosition(
    int BucketRank,
    DateTime ActionableAt,
    Guid OrderId);

public interface IServerTaskProjector
{
    ServerServiceTaskDto Project(Order order, DateTime serverTime);
    List<ServerServiceTaskDto> FilterAndSort(IEnumerable<ServerServiceTaskDto> tasks, string bucket);
}
