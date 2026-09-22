using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Durable, auditable request for an Admin or Cashier to collect a service-session bill.
/// The operation id is the Server client's replay key; the requested amount and currency are
/// captured from the locked server-side bill, never accepted from the request body.
/// </summary>
public class TableServicePaymentHandoff : Entity
{
    public Guid ServiceSessionId { get; set; }
    public Guid OperationId { get; set; }
    public int ExpectedVersion { get; set; }
    public decimal RequestedAmount { get; set; }
    public string? RequestedCurrency { get; set; }
    public TableServicePaymentHandoffStatus Status { get; set; } = TableServicePaymentHandoffStatus.Requested;
    public DateTime RequestedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    /// <summary>Primary key of the committed TableBillPaymentOperation that settled this request.</summary>
    public Guid? ResolvedPaymentOperationId { get; set; }
    public string? ResolvedBy { get; set; }
    public Guid? CancellationOperationId { get; set; }
    public int? CancellationExpectedVersion { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }

    public virtual TableServiceSession ServiceSession { get; set; } = null!;
}
