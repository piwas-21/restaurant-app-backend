using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>
/// Caches Apple's signing keys for the whole process (BACKEND-NOTES §4.1: "keys CACHED and
/// refreshed, do not fetch per request").
/// <para>
/// Registered as a SINGLETON, which is why it resolves <see cref="IAppleJwksFetcher"/> from the
/// service provider per fetch rather than holding one: the fetcher is a typed <c>HttpClient</c>
/// client and therefore transient, and capturing one in a singleton would pin a single message
/// handler for the lifetime of the process.
/// </para>
/// </summary>
public sealed class AppleSigningKeyProvider : IAppleSigningKeyProvider
{
    /// <summary>
    /// Floor between two forced refreshes. A forced refresh is triggered by an incoming token
    /// naming an unknown <c>kid</c> — which anyone can send — so without this floor a crafted
    /// token would be a free way to make us call Apple once per request.
    /// </summary>
    private static readonly TimeSpan MinimumForcedRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IServiceProvider _services;
    private readonly AppleAuthSettings _settings;
    private readonly ILogger<AppleSigningKeyProvider> _logger;
    private readonly TimeProvider _timeProvider;

    private IReadOnlyCollection<SecurityKey> _keys = Array.Empty<SecurityKey>();
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public AppleSigningKeyProvider(
        IServiceProvider services,
        IOptions<AppleAuthSettings> settings,
        ILogger<AppleSigningKeyProvider> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _services = services;
        _settings = settings.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!NeedsFetch(forceRefresh))
        {
            return _keys;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: a concurrent login may have refreshed while we queued.
            if (!NeedsFetch(forceRefresh))
            {
                return _keys;
            }

            var fetcher = _services.GetRequiredService<IAppleJwksFetcher>();
            var keySet = await fetcher.FetchAsync(cancellationToken);

            _keys = keySet.GetSigningKeys().ToList();
            _fetchedAt = _timeProvider.GetUtcNow();
            _logger.LogInformation("Fetched {KeyCount} Apple signing key(s) from {JwksUri}",
                _keys.Count, _settings.JwksUri);

            return _keys;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A serving cache outlives a bad fetch: Apple being briefly unreachable must not
            // lock every user out. With no cache at all the caller gets an empty set, which
            // fails the signature check closed.
            _logger.LogError(ex, "Could not fetch Apple signing keys from {JwksUri}", _settings.JwksUri);
            return _keys;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool NeedsFetch(bool forceRefresh)
    {
        if (_keys.Count == 0)
        {
            return true;
        }

        var age = _timeProvider.GetUtcNow() - _fetchedAt;

        return forceRefresh
            ? age >= MinimumForcedRefreshInterval
            : age >= TimeSpan.FromMinutes(_settings.JwksCacheMinutes);
    }
}
