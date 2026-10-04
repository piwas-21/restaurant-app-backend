using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public enum OrderBillingSnapshotOwnerSlot
{
    Earning = 1,
    Redemption = 2
}

public enum OrderBillingSnapshotOwnerDisposition
{
    Linked = 1,
    Erased = 2
}

/// <summary>Mutable current-owner link kept outside the immutable financial snapshot.</summary>
public sealed class OrderBillingSnapshotOwnerLink : Entity
{
    public Guid OrderId { get; set; }
    public OrderBillingSnapshotOwnerSlot Slot { get; set; }
    public Guid? UserId { get; set; }
    public OrderBillingSnapshotOwnerDisposition Disposition { get; set; }
    public DateTime? ErasedAt { get; set; }
    public string? ErasureTransactionId { get; set; }
}
