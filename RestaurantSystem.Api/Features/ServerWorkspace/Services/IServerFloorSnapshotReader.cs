using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

public interface IServerFloorSnapshotReader
{
    Task<ServerFloorSnapshotDto> ReadAsync(CancellationToken cancellationToken);
}
