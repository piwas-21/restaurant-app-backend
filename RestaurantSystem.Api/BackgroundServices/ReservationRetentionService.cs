using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

// GDPR storage-limitation (Art. 5(1)(e)). A reservation is NOT a financial record,
// so its denormalized contact snapshot must not be kept indefinitely (unlike
// orders/payments, which are retained for bookkeeping). This sweeper anonymizes
// CustomerName/Email/Phone/SpecialRequests/Notes on reservations whose
// ReservationDate is older than the configured window; the row itself (table,
// date, guest count, status) is retained for occupancy history — only the PII
// leaves. Sibling of BasketCleanupService / AccountCleanupService.
//
// Data-loss class (CLAUDE.md §9): DISABLED unless ReservationRetention:Enabled=true,
// so the capability deploys inert and no PII is scrubbed until an owner turns it on
// and confirms the window in box config.
public class ReservationRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReservationRetentionService> _logger;
    private readonly ReservationRetentionSettings _settings;

    // Tombstone for the NOT NULL contact columns (Name/Email/Phone are IsRequired in
    // ReservationConfiguration); nullable columns (SpecialRequests/Notes) are cleared
    // to null. Matches AccountCleanupService so an account-erased and a
    // retention-scrubbed reservation look identical.
    private const string Erased = "[erased]";

    public ReservationRetentionService(
        IServiceProvider serviceProvider,
        ILogger<ReservationRetentionService> logger,
        IOptions<ReservationRetentionSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(
                "ReservationRetentionService is disabled (ReservationRetention:Enabled=false) — no reservation PII will be scrubbed.");
            return;
        }

        if (_settings.RetentionMonths <= 0)
        {
            // A non-positive window would scrub effectively EVERY reservation — refuse
            // to run rather than mass-erase on a fat-fingered config. Fix and restart.
            _logger.LogError(
                "ReservationRetentionService disabled: RetentionMonths must be positive but was {Months}.",
                _settings.RetentionMonths);
            return;
        }

        // Clamp the interval to ≥1h so a 0/negative misconfiguration can neither
        // tight-loop the DB nor throw out of ExecuteAsync (which, under the default
        // StopHost behavior, would take the API down).
        var sweepInterval = TimeSpan.FromHours(Math.Max(1, _settings.SweepIntervalHours));

        _logger.LogInformation(
            "ReservationRetentionService starting — scrubbing contact PII from reservations older than {Months} months every {Hours}h.",
            _settings.RetentionMonths, sweepInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScrubExpiredReservations(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // OperationCanceledException on shutdown (stoppingToken cancelled mid-sweep)
                // is normal, not an error — let it propagate out and end the loop quietly
                // instead of logging a false-positive error every time the host stops.
                _logger.LogError(ex, "Error occurred while scrubbing expired-reservation PII.");
            }

            await Task.Delay(sweepInterval, stoppingToken);
        }
    }

    // Returns the number of reservations scrubbed this pass (0 once the backlog is
    // drained — the idempotency signal the tests assert on).
    internal async Task<int> ScrubExpiredReservations(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Compute the cutoff in C# (a concrete DateTime) so the query stays a plain,
        // fully-translatable comparison — never push AddMonths into EF.
        var cutoff = DateTime.UtcNow.AddMonths(-_settings.RetentionMonths);

        // Already-scrubbed rows (CustomerEmail == Erased) are excluded so the sweep is
        // idempotent and doesn't rewrite the same rows every run. CustomerId is left
        // intact — the account link is governed by the account lifecycle
        // (AccountCleanupService nulls it on deletion); here we only drop the redundant
        // contact snapshot on the reservation itself. The set-based ExecuteUpdate is
        // chunked so the first run after enablement — potentially a large historical
        // backlog — can't hold one long transaction / lock the table / exhaust the WAL.
        // We page the IDs first (bounded to batchSize Guids in memory) rather than
        // `.Take().ExecuteUpdate()`, which doesn't translate to a bounded UPDATE on PostgreSQL.
        const int batchSize = 1000;
        var totalScrubbed = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var expiredIds = await context.Reservations
                .Where(r => r.ReservationDate < cutoff && r.CustomerEmail != Erased)
                .Select(r => r.Id)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (expiredIds.Count == 0)
            {
                break;
            }

            var scrubbed = await context.Reservations
                .Where(r => expiredIds.Contains(r.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CustomerName, Erased)
                    .SetProperty(r => r.CustomerEmail, Erased)
                    .SetProperty(r => r.CustomerPhone, Erased)
                    .SetProperty(r => r.SpecialRequests, (string?)null)
                    .SetProperty(r => r.Notes, (string?)null), stoppingToken);

            totalScrubbed += scrubbed;

            // A short page means the backlog is drained — stop. (The `!= Erased` filter
            // guarantees forward progress: every scrubbed row drops out of the predicate.)
            if (expiredIds.Count < batchSize)
            {
                break;
            }
        }

        if (totalScrubbed > 0)
        {
            _logger.LogInformation(
                "Scrubbed contact PII from {Count} reservations older than {Months} months.",
                totalScrubbed, _settings.RetentionMonths);
        }

        return totalScrubbed;
    }
}
