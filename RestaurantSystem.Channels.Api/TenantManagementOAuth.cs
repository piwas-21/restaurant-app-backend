namespace RestaurantSystem.Channels.Api;

public sealed class TenantManagementOAuth(TenantManagementContext context,
    ITenantOAuthStateCoordinator coordinator) : ITenantManagementOAuth
{
    public Task<TenantOAuthStart> Start(Guid actorId, bool enableOrderAcceptance, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        return coordinator.Start(actorId, enableOrderAcceptance, cancellationToken);
    }

    public Task<TenantOAuthStatus> Read(Guid flowId, Guid actorId, CancellationToken cancellationToken)
        => coordinator.Read(flowId, actorId, cancellationToken);

    public Task<TenantOAuthCallback?> CompleteIfKnown(string state, string code, string error,
        CancellationToken cancellationToken)
        => coordinator.CompleteIfKnown(state, code, error, cancellationToken);
}
