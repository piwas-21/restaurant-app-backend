using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerFloorSnapshotQuery;

public sealed record GetServerFloorSnapshotQuery : IQuery<ApiResponse<ServerFloorSnapshotDto>>;

public sealed class GetServerFloorSnapshotQueryHandler
    : IQueryHandler<GetServerFloorSnapshotQuery, ApiResponse<ServerFloorSnapshotDto>>
{
    private readonly IServerFloorSnapshotReader _reader;

    public GetServerFloorSnapshotQueryHandler(IServerFloorSnapshotReader reader) => _reader = reader;

    public async Task<ApiResponse<ServerFloorSnapshotDto>> Handle(
        GetServerFloorSnapshotQuery query, CancellationToken cancellationToken) =>
        ApiResponse<ServerFloorSnapshotDto>.SuccessWithData(
            await _reader.ReadAsync(cancellationToken));
}
