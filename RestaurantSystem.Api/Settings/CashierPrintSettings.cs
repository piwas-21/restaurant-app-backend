using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class CashierPrintSettings
{
    public const string SectionName = "CashierPrinting";

    [Range(1, 1440)]
    public int CapabilityFreshnessMinutes { get; set; } = 5;

    [Range(1, 365)]
    public int QueueLifetimeDays { get; set; } = 1;
}
