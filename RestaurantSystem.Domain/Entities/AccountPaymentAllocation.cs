using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable monetary scope; an amount contribution does not assert item ownership.</summary>
public sealed class AccountPaymentAllocation : Entity
{
    public Guid AttemptId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public Guid? OrderPaymentId { get; set; }
    public int StartOrdinal { get; set; }
    public int UnitCount { get; set; }
    public long MinorPerUnit { get; set; }
    public long AmountMinor { get; set; }
    public AccountPaymentAttempt? Attempt { get; set; }
    public Order? Order { get; set; }
    public OrderItem? OrderItem { get; set; }
    public OrderPayment? OrderPayment { get; set; }
}
