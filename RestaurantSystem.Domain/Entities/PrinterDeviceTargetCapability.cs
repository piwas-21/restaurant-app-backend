using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Non-secret per-target capability reported by a printer-app installation.</summary>
public class PrinterDeviceTargetCapability : Entity
{
    public required string DeviceId { get; set; }
    public DevicePrintTarget Target { get; set; }
    public bool IsSupported { get; set; }
    public bool IsConfigured { get; set; }
    public bool AutoPrintEnabled { get; set; }
    public string? PrinterName { get; set; }
    public DateTime ReportedAt { get; set; }
}
