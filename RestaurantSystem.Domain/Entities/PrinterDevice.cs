using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A deployed printer-app installation reporting fleet-observability telemetry. Upserted by
/// <see cref="DeviceId"/> on each heartbeat. Online/offline and stale-feed status are DERIVED
/// (now − LastHeartbeatAt / LastSuccessfulPollAt), never stored. Holds only non-secret config —
/// never the printer-feed API key. See docs/plans/PRINTER-APP-FLEET-OBSERVABILITY-PLAN.md.
/// </summary>
public class PrinterDevice : Entity
{
    /// <summary>Stable per-install id the app sends as the <c>X-Device-Id</c> header; distinguishes
    /// multiple devices at one site (e.g. kitchen vs cashier tablet) that share the tenant's single
    /// <c>X-Api-Key</c>.</summary>
    public required string DeviceId { get; set; }

    /// <summary>Human label for the admin panel, e.g. "Kitchen tablet".</summary>
    public string? Label { get; set; }

    /// <summary>Control-plane tenant slug this device self-reports (for the sofra roll-up).</summary>
    public string? TenantSlug { get; set; }

    /// <summary>Runtime platform, e.g. "Android" or "WinUI".</summary>
    public string? Platform { get; set; }

    /// <summary>App display version, e.g. "1.0.18".</summary>
    public string? AppVersion { get; set; }

    /// <summary>Timestamp of the most recent heartbeat (drives online/offline).</summary>
    public DateTime LastHeartbeatAt { get; set; }

    /// <summary>Whether the order-polling feed was listening at the last heartbeat.</summary>
    public bool FeedRunning { get; set; }

    /// <summary>Last time the device successfully polled the order feed (drives stale-feed detection).</summary>
    public DateTime? LastSuccessfulPollAt { get; set; }

    /// <summary>Configured backend base URL (non-secret), for the admin config view.</summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Configured kitchen printer target (IP or spooler name; non-secret).</summary>
    public string? KitchenPrinter { get; set; }

    /// <summary>Configured cashier printer target (IP or spooler name; non-secret).</summary>
    public string? CashierPrinter { get; set; }
}
