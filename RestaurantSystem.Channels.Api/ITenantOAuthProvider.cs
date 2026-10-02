using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed record TenantOAuthPreparation(TenantOAuthFlow Flow, string AuthorizationUrl);

public interface ITenantOAuthProvider
{
    Task<TenantOAuthPreparation> Prepare(Guid flowId, Guid actorId, bool enableOrderAcceptance,
        CancellationToken cancellationToken);
    string HashState(string state);
    Task Connect(TenantOAuthFlow flow, string code, CancellationToken cancellationToken);
}
