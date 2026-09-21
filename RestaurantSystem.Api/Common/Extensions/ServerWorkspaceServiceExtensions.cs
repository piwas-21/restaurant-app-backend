using RestaurantSystem.Api.Features.ServerWorkspace.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class ServerWorkspaceServiceExtensions
{
    public static IServiceCollection AddServerWorkspaceServices(this IServiceCollection services)
    {
        services.AddScoped<IServerFloorSnapshotReader, ServerFloorSnapshotReader>();
        return services;
    }
}
