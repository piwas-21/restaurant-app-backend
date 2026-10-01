using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxSessions(IOptions<SandboxConsoleSettings> options, ISandboxCrypto crypto,
    IConsoleRepository repository, TimeProvider timeProvider) : ISandboxSessions
{
    public void RequireOrigin(HttpContext context)
    {
        if (!string.Equals(context.Request.Headers.Origin, new Uri(options.Value.PublicBaseUrl).GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            throw new ChannelConsoleException(403, "Open the console directly before performing this action.");
    }

    public async Task<string> Login(string accessKey, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || accessKey.Length is < 32 or > 128 || !crypto.MatchesHash(accessKey, options.Value.OwnerAccessHash))
            throw new ChannelConsoleException(401, "The console access key is invalid.");
        var session = crypto.RandomToken();
        await repository.CreateSession(crypto.Hash(session), timeProvider.GetUtcNow().AddMinutes(options.Value.SessionMinutes), cancellationToken);
        return session;
    }

    public async Task<string> Require(HttpContext context, bool mutating, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) throw new ChannelConsoleException(404, "The sandbox console is disabled.");
        if (mutating) RequireOrigin(context);
        var cookie = context.Request.Cookies[SandboxConsoleSettings.CookieName] ?? string.Empty;
        if (cookie.Length is < 32 or > 128 || !await repository.SessionExists(crypto.Hash(cookie), timeProvider.GetUtcNow(), cancellationToken))
            throw new ChannelConsoleException(401, "Sign in to the sandbox console.");
        return crypto.Hash(cookie);
    }

    public Task Logout(string sessionHash, CancellationToken cancellationToken) => repository.EndSession(sessionHash, cancellationToken);
}
