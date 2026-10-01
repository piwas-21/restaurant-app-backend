namespace RestaurantSystem.Channels.Api;

public sealed record AuthorizationGrant(string Verifier, bool EnableTesting);

public interface ISandboxAuthorization
{
    Task<string> Start(string sessionHash, bool enableTesting, CancellationToken cancellationToken);
    Task<AuthorizationGrant> Claim(string sessionHash, string state, CancellationToken cancellationToken);
}
