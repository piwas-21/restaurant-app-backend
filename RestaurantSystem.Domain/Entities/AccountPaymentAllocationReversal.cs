using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable exact refund of part of one captured account allocation range.</summary>
public sealed class AccountPaymentAllocationReversal : Entity
{
    public Guid AllocationId { get; set; }
    public Guid RefundLegId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public int StartOrdinal { get; set; }
    public int UnitCount { get; set; }
    public long MinorPerUnit { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public DateTime ReversedAt { get; set; }
}
