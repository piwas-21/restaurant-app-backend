namespace RestaurantSystem.Channels.Api;

public sealed record TenantOAuthStart(Guid FlowId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
public sealed record TenantOAuthStatus(Guid FlowId, string Status, Guid StoreId, bool StoreConfirmed,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt, string? ErrorCode);
public sealed record TenantOAuthCallback(Guid FlowId, string ReturnUrl);

public interface ITenantManagementOAuth
{
    Task<TenantOAuthStart> Start(Guid actorId, bool enableOrderAcceptance, CancellationToken cancellationToken);
    Task<TenantOAuthStatus> Read(Guid flowId, Guid actorId, CancellationToken cancellationToken);
    Task<TenantOAuthCallback?> CompleteIfKnown(string state, string code, string error, CancellationToken cancellationToken);
}
