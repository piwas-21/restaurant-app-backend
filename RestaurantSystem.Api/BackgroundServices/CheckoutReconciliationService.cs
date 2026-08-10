using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.BackgroundServices;

/// <summary>
/// Drives the two Stripe checkout sweeps (SOFRA-PAYMENTS-PLAN S7). Loop only — every decision lives
/// in <see cref="ICheckoutExpirySweep"/> and <see cref="ICheckoutClearanceSweep"/>, which are
/// ordinary scoped services and therefore testable without a host.
/// </summary>
/// <remarks>
/// <b>Data-loss class (CLAUDE.md §9): the expiry sweep CANCELS ORDERS.</b> It is disabled unless
/// <c>CheckoutReconciliation:Enabled=true</c>, so it deploys inert to the whole fleet — matching the
/// Stripe integration it backs, which is itself off everywhere today.
///
/// <para>
/// It exists because there is no webhook in v1: the platform may not register one on a connected
/// account (measured, plan §4). Settlement therefore has two callers, the diner's return trip and
/// this poll, and the poll is the only one that ever runs for a diner who closes the tab.
/// </para>
/// </remarks>
public class CheckoutReconciliationService : BackgroundService
{
    /// <summary>
    /// Interval floor. A 0 or negative configured value would spin this loop against the Stripe API
    /// as fast as the network allows; clamping is what keeps a fat-fingered config from becoming a
    /// rate-limit incident, and it cannot throw out of ExecuteAsync and take the API down with it.
    /// </summary>
    private const int MinimumIntervalSeconds = 5;

    /// <summary>
    /// Batch ceiling. Each session in a batch is one sequential Stripe read, so an unbounded value
    /// would defeat the very thing the batch exists for and turn the first pass after an outage into
    /// a rate-limit incident. Symmetric with the interval floor above.
    /// </summary>
    private const int MaximumBatchSize = 500;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CheckoutReconciliationService> _logger;
    private readonly CheckoutReconciliationSettings _settings;

    public CheckoutReconciliationService(
        IServiceProvider serviceProvider,
        ILogger<CheckoutReconciliationService> logger,
        IOptions<CheckoutReconciliationSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(
                "CheckoutReconciliationService is disabled (CheckoutReconciliation:Enabled=false) — "
                + "no checkout session will be settled or expired by polling.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds, _settings.SweepIntervalSeconds));
        var batchSize = Math.Clamp(_settings.BatchSize, 1, MaximumBatchSize);

        _logger.LogInformation(
            "CheckoutReconciliationService starting — sweeping up to {BatchSize} checkout session(s) every {Seconds}s.",
            batchSize, interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(batchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A cancellation on shutdown is normal, not an error — let it out of the loop
                // quietly instead of logging a false error every time the host stops. Everything
                // else is swallowed on purpose: one bad sweep (a Stripe outage, a poison row) must
                // not end the loop, or the backstop silently stops backstopping.
                _logger.LogError(ex, "Error occurred while reconciling Stripe checkout sessions.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    /// <remarks>
    /// The two sweeps share a scope but not a fate: the clearance sweep runs even if the expiry
    /// sweep threw, because they answer independent questions and the one that failed is not
    /// necessarily the one with a problem.
    /// </remarks>
    private async Task SweepAsync(int batchSize, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();

        // Nothing to reconcile against. Every Stripe call in both sweeps would throw
        // BadRequestException from BuildRequestOptions, once per session, every interval — noise
        // that hides a real failure. Enabled here without Stripe configured is a misconfiguration,
        // and the honest response is to do nothing quietly.
        if (!scope.ServiceProvider.GetRequiredService<IStripeGateway>().IsConfigured)
        {
            return;
        }

        var expiry = scope.ServiceProvider.GetRequiredService<ICheckoutExpirySweep>();
        var clearance = scope.ServiceProvider.GetRequiredService<ICheckoutClearanceSweep>();

        try
        {
            await expiry.RunAsync(batchSize, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Checkout expiry sweep failed.");
        }

        await clearance.RunAsync(batchSize, stoppingToken);
    }
}
