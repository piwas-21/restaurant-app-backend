using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using Stripe;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
/// <remarks>
/// <b>One read, cached, admin-only, and optional.</b> Nothing on a diner-facing path ever reaches
/// this: <c>/api/payments/availability</c> stays anonymous and answers from configuration alone
/// (S8), and this is called only by the admin onboarding endpoint. So the cache is not there to
/// protect a hot path — it is there because an admin refreshing the payments tab must not turn into
/// a read flood against the tenant's connected account, which Stripe answers with a
/// <c>rate_limit</c> error that would also hit the settle path and the reconciler on the same key.
/// <para>
/// Singleton, holding one snapshot, because there is exactly one connected account per instance —
/// the account id is process-lifetime configuration, like <see cref="IStripeGateway"/> itself.
/// </para>
/// </remarks>
public class StripeAccountClient : IStripeAccountClient
{
    /// <summary>
    /// How long a snapshot is reused. Long enough that a human clicking around cannot generate
    /// traffic; short enough that a restaurant who has just finished Stripe's form sees the change
    /// without being told to wait. KYC completes on a scale of days, so precision buys nothing here.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly IStripeGateway _gateway;
    private readonly StripeSettings _settings;
    private readonly ILogger<StripeAccountClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;

    private StripeConnectedAccount? _snapshot;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    public StripeAccountClient(
        IStripeGateway gateway,
        IOptions<StripeSettings> settings,
        ILogger<StripeAccountClient> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
        _time = timeProvider;
    }

    public async Task<StripeConnectedAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken)
    {
        // Not configured means there is no account to read and no key to read it with. Asking Stripe
        // would be a guaranteed 401 on every tenant in the fleet today.
        if (!_gateway.IsConfigured) return null;

        if (IsFresh()) return _snapshot;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: several admins loading the tab at once must produce one
            // call, not one each.
            if (IsFresh()) return _snapshot;

            _snapshot = await FetchAsync(cancellationToken);
            // Stamped even when the fetch FAILED, so a refused permission is retried every five
            // minutes rather than on every request. A refusal is a standing condition — the key
            // either carries `Accounts → read` or it does not — and hammering it turns one
            // misconfiguration into sustained traffic.
            _readAt = _time.GetUtcNow();
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh() => _time.GetUtcNow() - _readAt < CacheFor;

    private async Task<StripeConnectedAccount?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var service = new AccountService(_gateway.Client);
            var account = await service.GetAsync(
                _settings.ConnectedAccountId,
                requestOptions: _gateway.BuildRequestOptions(),
                cancellationToken: cancellationToken);

            return new StripeConnectedAccount(
                account.Id,
                account.ChargesEnabled,
                account.Requirements?.CurrentlyDue?.Count ?? 0);
        }
        catch (StripeException e)
        {
            // Warning, not error: the box key is not required to carry `Accounts → read` (§9 P0(b)
            // is the decision to grant it), so a refusal here is a configuration this deployment is
            // allowed to be in. It is LOGGED because the alternative — a tab that silently reports
            // less than it could, forever — is indistinguishable from the feature not existing.
            //
            // The exception is passed, not just its shape: a refusal nobody can diagnose is only
            // marginally better than a silent one, and Stripe's own message is the fastest way to
            // tell a missing permission from a revoked key — which the STATUS cannot, because plan
            // §4 measured an Access-policy block as a 401 rather than a 403. Nothing here is a
            // credential: the worst it can name is the connected account id, which is a public-side
            // identifier that appears in Stripe's own dashboard URLs.
            _logger.LogWarning(
                e,
                "Stripe account read refused ({Status}/{Code}); reporting configuration only.",
                (int)e.HttpStatusCode,
                e.StripeError?.Code ?? "none");
            return null;
        }
    }
}
