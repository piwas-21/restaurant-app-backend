namespace RestaurantSystem.Api.Settings;

// Governs DeviceTelemetryRetentionService, which PURGES fleet-observability telemetry
// (DeviceEvents + DeviceOrderReceipts) older than the window. This data is diagnostic and grows
// unbounded with device activity; old rows are never needed (missed-order reconciliation looks back
// hours, admin views show recent history). Data-loss class (CLAUDE.md §9): the 30-day window +
// enablement were OWNER-APPROVED 2026-07-20 — unlike ReservationRetention (PII, off by default),
// this is bounded non-PII diagnostics whose whole purpose is to keep the tables from growing forever.
public class DeviceTelemetryRetentionSettings
{
    // Master switch. While false the sweeper starts, logs that it is disabled, and purges nothing.
    public bool Enabled { get; set; } = true;

    // Days after ingest (CreatedAt) before a device event / receipt is deleted.
    public int RetentionDays { get; set; } = 30;

    // How often the sweeper runs.
    public int SweepIntervalHours { get; set; } = 24;
}
