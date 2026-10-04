namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

public interface IServerFloorSnapshotPolicy
{
    bool TableVisitReadinessEnabled { get; }
    bool CanStartCollection { get; }
}
