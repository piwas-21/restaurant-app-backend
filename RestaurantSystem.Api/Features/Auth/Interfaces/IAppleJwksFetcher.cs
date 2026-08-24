using Microsoft.IdentityModel.Tokens;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

/// <summary>
/// Fetches Apple's public signing keys (JWKS). One HTTP call, no caching — caching is
/// <see cref="IAppleSigningKeyProvider"/>'s job, so that a key set survives the transient
/// lifetime of the typed <c>HttpClient</c> this is registered with.
/// </summary>
public interface IAppleJwksFetcher
{
    Task<JsonWebKeySet> FetchAsync(CancellationToken cancellationToken);
}
