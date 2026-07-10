namespace RestaurantSystem.Api.Settings;

// Governs ReservationRetentionService, which anonymizes the contact snapshot on
// reservations older than the window (GDPR storage-limitation, Art. 5(1)(e)).
// Data-loss class (CLAUDE.md §9): DISABLED by default — enabling it and choosing
// the window is a deliberate, owner-approved event set via config on the box.
public class ReservationRetentionSettings
{
    // Master switch. While false the sweeper starts, logs that it is disabled, and
    // scrubs nothing.
    public bool Enabled { get; set; }

    // Months after ReservationDate before a reservation's contact PII is scrubbed.
    public int RetentionMonths { get; set; } = 24;

    // How often the sweeper runs.
    public int SweepIntervalHours { get; set; } = 24;
}
