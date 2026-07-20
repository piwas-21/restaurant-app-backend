using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

// Purges fleet-observability telemetry older than the configured window. DeviceEvents (diagnostic
// error/warning/health log) and DeviceOrderReceipts (print acks) both grow unbounded with device
// activity and are only needed recently, so deleting old rows keeps the tables bounded. Sibling of
// ReservationRetentionService / BasketCleanupService.
//
// Data-loss class (CLAUDE.md §9): the window (30 days) + enablement were OWNER-APPROVED 2026-07-20.
// Still guarded — a non-positive RetentionDays refuses to run rather than mass-delete on a
// fat-fingered config.
public class DeviceTelemetryRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceTelemetryRetentionService> _logger;
    private readonly DeviceTelemetryRetentionSettings _settings;

    public DeviceTelemetryRetentionService(
        IServiceProvider serviceProvider,
        ILogger<DeviceTelemetryRetentionService> logger,
        IOptions<DeviceTelemetryRetentionSettings> settings)
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
                "DeviceTelemetryRetentionService is disabled (DeviceTelemetryRetention:Enabled=false) — no telemetry will be purged.");
            return;
        }

        if (_settings.RetentionDays <= 0)
        {
            // A non-positive window would delete effectively EVERY row — refuse to run rather than
            // mass-delete on a misconfiguration. Fix and restart.
            _logger.LogError(
                "DeviceTelemetryRetentionService disabled: RetentionDays must be positive but was {Days}.",
                _settings.RetentionDays);
            return;
        }

        // Clamp the interval to ≥1h so a 0/negative misconfiguration can't tight-loop the DB.
        var sweepInterval = TimeSpan.FromHours(Math.Max(1, _settings.SweepIntervalHours));

        _logger.LogInformation(
            "DeviceTelemetryRetentionService starting — purging device telemetry older than {Days} days every {Hours}h.",
            _settings.RetentionDays, sweepInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredTelemetry(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred while purging expired device telemetry.");
            }

            await Task.Delay(sweepInterval, stoppingToken);
        }
    }

    // Returns total rows deleted this pass (0 once drained — the idempotency signal the tests assert).
    internal async Task<int> PurgeExpiredTelemetry(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Measured on CreatedAt (backend ingest time), not the device-supplied OccurredAt/ReceivedAt:
        // a device with a skewed clock must not cause premature or delayed deletion.
        var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);

        var events = await PurgeOlderThan(context.DeviceEvents, cutoff, stoppingToken);
        var receipts = await PurgeOlderThan(context.DeviceOrderReceipts, cutoff, stoppingToken);

        var total = events + receipts;
        if (total > 0)
        {
            _logger.LogInformation(
                "Purged {Events} device events + {Receipts} print receipts older than {Days} days.",
                events, receipts, _settings.RetentionDays);
        }

        return total;
    }

    // Page ids then delete by id set: a bounded Take(...).ExecuteDelete doesn't translate to a bounded
    // DELETE on PostgreSQL, and chunking keeps the first post-enable run (a large backlog) off one long
    // transaction. Forward progress: every deleted row drops out of the CreatedAt predicate.
    private static async Task<int> PurgeOlderThan<T>(
        DbSet<T> set, DateTime cutoff, CancellationToken stoppingToken) where T : Entity
    {
        const int batchSize = 1000;
        var total = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var expiredIds = await set
                .Where(e => e.CreatedAt < cutoff)
                .Select(e => e.Id)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (expiredIds.Count == 0)
            {
                break;
            }

            total += await set
                .Where(e => expiredIds.Contains(e.Id))
                .ExecuteDeleteAsync(stoppingToken);

            if (expiredIds.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }
}
