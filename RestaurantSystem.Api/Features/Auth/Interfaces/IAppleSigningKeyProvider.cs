using Microsoft.IdentityModel.Tokens;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

/// <summary>
/// Apple's current signing keys, cached across requests. The test double for the whole
/// Apple-verification path: supply a key set and the validator can be driven end to end
/// without touching the network.
/// </summary>
public interface IAppleSigningKeyProvider
{
    /// <param name="forceRefresh">
    /// Ask for a re-fetch because a token named a <c>kid</c> the cache does not hold — Apple
    /// rotates keys. Implementations may still answer from cache to bound how often an
    /// attacker-supplied token can make us call Apple.
    /// </param>
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(bool forceRefresh, CancellationToken cancellationToken);
}
