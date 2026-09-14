using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A printer-app's report of how one logical job printed to one target. Legacy order receipts are
/// upserted by <c>(OrderId, DeviceId, Target)</c>. Additive update jobs use
/// <c>(DeviceId, JobId, Revision, Target)</c>, allowing later note revisions to coexist with the
/// original order receipt.
/// </summary>
public class DeviceOrderReceipt : Entity
{
    /// <summary>Reporting device's stable per-install id (the <c>X-Device-Id</c> header). Not a
    /// foreign key to <see cref="PrinterDevice"/>: an ack may arrive before the device's first
    /// heartbeat, and telemetry ingestion must never fail on referential ordering.</summary>
    public required string DeviceId { get; set; }

    /// <summary>The order this job is for. Plain id (indexed, not an FK) — the Devices feature
    /// stays decoupled from the Order aggregate; reconciliation joins on this value.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Stable identity of an additive job. Null means the legacy order receipt contract;
    /// old clients do not send a job id.</summary>
    public Guid? JobId { get; set; }

    /// <summary>Stable revision within a job. Required with <see cref="JobId"/> for update jobs;
    /// null on legacy order receipts.</summary>
    public int? Revision { get; set; }

    /// <summary>Logical work represented by the ack. Null is retained for legacy acknowledgements;
    /// new clients send <see cref="DevicePrintJobType.Update"/> for note-update tickets.</summary>
    public DevicePrintJobType? JobType { get; set; }

    /// <summary>Which logical printer destination the job was routed to.</summary>
    public DevicePrintTarget Target { get; set; }

    /// <summary>Outcome for this job/target on this device.</summary>
    public DevicePrintStatus Status { get; set; }

    /// <summary>When the device received the job from the feed.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>When the job finished printing (null while Received / on Failed / Skipped).</summary>
    public DateTime? PrintedAt { get; set; }

    /// <summary>Failure detail when <see cref="Status"/> is <c>Failed</c> (non-PII).</summary>
    public string? FailureReason { get; set; }

    /// <summary>Number of copies printed to this target.</summary>
    public int Copies { get; set; }
}
