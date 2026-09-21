using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Operational readiness policy for durable order routing.</summary>
public sealed class OrderRoutingSettings
{
    public const string SectionName = "OrderRouting";

    [Range(1, 60)]
    public int HeartbeatFreshnessMinutes { get; set; } = 5;
}
