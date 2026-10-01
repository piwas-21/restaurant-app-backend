using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxAuthorization(IOptions<UberWebhookSettings> webhookOptions, IOptions<SandboxConsoleSettings> consoleOptions,
    IConsoleRepository repository, ISandboxCrypto crypto, TimeProvider timeProvider) : ISandboxAuthorization
{
    public async Task<string> Start(string sessionHash, bool enableTesting, CancellationToken cancellationToken)
    {
        var state = crypto.RandomToken();
        var verifier = crypto.RandomToken();
        await repository.SaveAuthorization(new(crypto.Hash(state), sessionHash, webhookOptions.Value.StoreIds.Single(),
            crypto.Protect(verifier, $"oauth:{crypto.Hash(state)}"), timeProvider.GetUtcNow().AddMinutes(consoleOptions.Value.AuthorizationMinutes), enableTesting), cancellationToken);
        return QueryHelpers.AddQueryString(consoleOptions.Value.AuthBaseUrl.TrimEnd('/') + "/oauth/v2/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = webhookOptions.Value.ClientId,
            ["response_type"] = "code",
            ["scope"] = "eats.pos_provisioning",
            ["redirect_uri"] = consoleOptions.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath,
            ["state"] = state,
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
        });
    }

    public async Task<AuthorizationGrant> Claim(string sessionHash, string state, CancellationToken cancellationToken)
    {
        var saved = await repository.ClaimAuthorization(crypto.Hash(state), sessionHash, timeProvider.GetUtcNow(), cancellationToken);
        if (saved is null || saved.StoreId != webhookOptions.Value.StoreIds.Single())
            throw new ChannelConsoleException(400, "Authorization expired or was already used. Start a new connection.");
        return new(crypto.Unprotect(saved.VerifierCipher, $"oauth:{saved.Hash}"), saved.EnableTesting);
    }
}
