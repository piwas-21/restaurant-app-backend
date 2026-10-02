namespace RestaurantSystem.Channels.Api;

public interface ITenantOAuthStateCoordinator
{
    Task<TenantOAuthStart> Start(Guid actorId, bool enableOrderAcceptance, CancellationToken cancellationToken);
    Task<TenantOAuthStatus> Read(Guid flowId, Guid actorId, CancellationToken cancellationToken);
    Task<TenantOAuthCallback?> CompleteIfKnown(string state, string code, string error,
        CancellationToken cancellationToken);
}
