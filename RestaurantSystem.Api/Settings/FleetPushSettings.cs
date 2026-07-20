namespace RestaurantSystem.Api.Settings;

// Governs FleetSummaryPushService, which POSTs a compact per-tenant fleet snapshot to the sofra
// control plane's /api/telemetry/fleet ingest route. Deploys INERT: the pusher stays disabled until
// Enabled=true AND SofraIngestUrl + Secret + TenantSlug are all set (owner config on the box). The
// Secret is the shared bearer that must match sofra's PRINTER_TELEMETRY_SECRET — never logged.
public class FleetPushSettings
{
    public bool Enabled { get; set; }
    public string SofraIngestUrl { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public int PushIntervalMinutes { get; set; } = 3;
    public int MissedOrderGraceMinutes { get; set; } = 15;
    public int MissedOrderLookbackHours { get; set; } = 24;
    public int RecentErrorWindowHours { get; set; } = 24;
}
