using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable client operation for one table-bill tender. A bill allocation creates several
/// <see cref="OrderPayment"/> rows, so its retry key cannot use OrderPayment.OperationId, which is
/// globally unique per tender.</summary>
public class TableBillPaymentOperation : Entity
{
    public Guid OperationId { get; set; }
    public int TableNumber { get; set; }

    /// <summary>Non-null for the durable service-session bill flow; null for legacy table-number tenders.</summary>
    public Guid? ServiceSessionId { get; set; }
    public int? ExpectedVersion { get; set; }
    public string? Currency { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    public string? PaymentNotes { get; set; }
    public virtual TableServiceSession? ServiceSession { get; set; }
    public virtual ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
}
