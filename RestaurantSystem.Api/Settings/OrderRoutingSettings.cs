using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Operational readiness policy for durable order routing.</summary>
public sealed class OrderRoutingSettings
{
    public const string SectionName = "OrderRouting";

    [Range(1, 60)]
    public int HeartbeatFreshnessMinutes { get; set; } = 5;

    /// <summary>Maximum number of orders or route rows processed in one reconciliation batch.</summary>
    [Range(1, 1000)]
    public int ProcessingBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum age of a queued required route that may be recovered independently of a printer's
    /// modified-since cursor. The one-hour default matches the deployed printer's persistent
    /// completed-order deduplication window; increasing it is an operator reconciliation decision.
    /// </summary>
    [Range(1, 24)]
    public int RequiredQueuedRouteRecoveryHours { get; set; } = 1;

    /// <summary>How long a paged printer-feed recovery cursor remains valid after its first page.</summary>
    [Range(1, 60)]
    public int RequiredQueuedRouteRecoveryCursorLifetimeMinutes { get; set; } = 15;
}
