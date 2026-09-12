namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Typed outcome of one print job on one device. Values 1-4 are retained for the legacy
/// printer-app contract. New clients should report the explicit lifecycle values appended below,
/// rather than treating legacy <c>Received</c>/<c>Printed</c> values as a complete state machine.</summary>
public enum DevicePrintStatus
{
    /// <summary>Legacy alias/semantics: the device accepted the old acknowledgement.</summary>
    Received = 1,

    /// <summary>Legacy alias/semantics: the device reported a successful print.</summary>
    Printed = 2,

    Failed = 3,

    /// <summary>Legacy skip result; new clients use <see cref="NotConfigured"/> when no route exists.
    /// </summary>
    Skipped = 4,

    /// <summary>The job is queued locally and has not been sent to the physical printer yet.</summary>
    Queued = 5,

    /// <summary>The job was sent to the physical printer and awaits a final outcome.</summary>
    Sent = 6,

    /// <summary>No physical printer is configured for the requested logical target.</summary>
    NotConfigured = 7,

    /// <summary>The device could not determine a more specific outcome.</summary>
    Unknown = 8,
}
