using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable financial snapshot with one server-owned printer claim. Ambiguous claims never replay.</summary>
public class CashierPrintJob : Entity
{
    public Guid? OrderId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public bool IsAutomatic { get; set; }
    public string Status { get; set; } = "Queued";
    public string? DeviceId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public virtual Order? Order { get; set; }
    public virtual TableServiceSession? ServiceSession { get; set; }
}
