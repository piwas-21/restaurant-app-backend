using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Immutable order snapshot recording how one order was handled during table occupancy recovery.
/// Legacy membership stays unassigned; only its physical table association is archived.
/// </summary>
public sealed class TableOccupancyRecoveryDisposition : Entity
{
    public Guid OperationId { get; set; }
    public Guid TableId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public TableOccupancyRecoveryDispositionKind Kind { get; set; }
    public bool WasLegacyUnassigned { get; set; }
    public OrderStatus OriginalStatus { get; set; }
    public PaymentStatus OriginalPaymentStatus { get; set; }
    public decimal OriginalTotal { get; set; }
    public decimal OriginalBillingCreditAmount { get; set; }
    public decimal OriginalTotalPaid { get; set; }
    public decimal OriginalRemainingAmount { get; set; }
    public bool WasKitchenReleased { get; set; }
    public bool HadRoutingHistory { get; set; }
    public DateTime RecordedAt { get; set; }
}
