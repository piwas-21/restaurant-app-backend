namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Outcome of printing one order to one target on a device. <c>Skipped</c> distinguishes
/// "no printer configured for this target" from a genuine <c>Failed</c> — the printer-app today
/// conflates both as a bare <c>true</c> (see fleet-observability plan).</summary>
public enum DevicePrintStatus
{
    Received = 1,
    Printed = 2,
    Failed = 3,
    Skipped = 4
}
