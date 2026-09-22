using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable server-owned state for one logical destination of an order.</summary>
public class OrderRoutingState : Entity
{
    public Guid OrderId { get; set; }
    public Guid JobId { get; set; }
    public int Revision { get; set; } = 1;
    public int Version { get; set; } = 1;
    public DevicePrintTarget Target { get; set; }
    /// <summary>
    /// Whether this destination is required for the order's kitchen work to be considered
    /// delivered. Kitchen routes are required; the cashier copy is optional because a missing
    /// cashier printer must not hide otherwise successful kitchen output.
    /// </summary>
    public bool IsRequired { get; set; } = true;
    public DevicePrintStatus Status { get; set; }
    public string? DeviceId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? LastAcknowledgedAt { get; set; }
    public virtual Order Order { get; set; } = null!;
}
