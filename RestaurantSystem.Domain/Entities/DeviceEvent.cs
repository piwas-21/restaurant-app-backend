using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// An append-only diagnostic event reported by a printer-app (error / warning / health note).
/// De-duplicated by <c>(DeviceId, ClientEventId)</c> so a <b>sequential</b> retry never
/// double-inserts (the outbox sends per-device single-flight; see the fleet-observability plan).
/// Carries only non-PII context — never raw order JSON or the printer-feed API key. Retained for a
/// bounded window by a background cleanup service (§9 data-loss class).
/// </summary>
public class DeviceEvent : Entity
{
    /// <summary>Reporting device's stable per-install id (the <c>X-Device-Id</c> header).</summary>
    public required string DeviceId { get; set; }

    /// <summary>Device-generated id for this event, unique per device — the idempotency key that
    /// makes at-least-once outbox delivery safe.</summary>
    public required string ClientEventId { get; set; }

    /// <summary>When the event occurred on the device (client clock, normalised to UTC on ingest).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Severity.</summary>
    public DeviceEventLevel Level { get; set; }

    /// <summary>Optional machine-readable code (e.g. "PRINTER_UNREACHABLE").</summary>
    public string? Code { get; set; }

    /// <summary>Human-readable message (non-PII).</summary>
    public required string Message { get; set; }

    /// <summary>Optional structured context, conventionally a small JSON object (whitelisted non-PII
    /// only). Stored as capped plain text — deliberately not <c>jsonb</c>: telemetry ingestion must
    /// never hard-fail on a malformed value the way a <c>jsonb</c> column would (that would wedge a
    /// retrying outbox — see docs/plans/PRINTER-APP-FLEET-OBSERVABILITY-PLAN.md).</summary>
    public string? Context { get; set; }
}
