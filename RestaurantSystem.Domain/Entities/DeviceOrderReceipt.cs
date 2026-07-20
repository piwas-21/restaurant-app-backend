using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A printer-app's report of how one order printed to one target. Upserted by
/// <c>(OrderId, DeviceId, Target)</c> so a sequential telemetry-outbox retry is idempotent
/// (the outbox sends per-device single-flight, so concurrent same-device batches don't occur).
/// The backend reconciles the served set (from the printer-feed) against these acks to detect
/// <b>missed</b> orders — the exact class of failure the 2026-07-19 incident hid. See
/// docs/plans/PRINTER-APP-FLEET-OBSERVABILITY-PLAN.md.
/// </summary>
public class DeviceOrderReceipt : Entity
{
    /// <summary>Reporting device's stable per-install id (the <c>X-Device-Id</c> header). Not a
    /// foreign key to <see cref="PrinterDevice"/>: an ack may arrive before the device's first
    /// heartbeat, and telemetry ingestion must never fail on referential ordering.</summary>
    public required string DeviceId { get; set; }

    /// <summary>The order this receipt is for. Plain id (indexed, not an FK) — the Devices feature
    /// stays decoupled from the Order aggregate; reconciliation joins on this value.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Which printer the order was routed to.</summary>
    public DevicePrintTarget Target { get; set; }

    /// <summary>Outcome for this order/target on this device.</summary>
    public DevicePrintStatus Status { get; set; }

    /// <summary>When the device received the order from the feed.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>When the order finished printing (null while Received / on Failed / Skipped).</summary>
    public DateTime? PrintedAt { get; set; }

    /// <summary>Failure detail when <see cref="Status"/> is <c>Failed</c> (non-PII).</summary>
    public string? FailureReason { get; set; }

    /// <summary>Number of copies printed to this target.</summary>
    public int Copies { get; set; }
}
