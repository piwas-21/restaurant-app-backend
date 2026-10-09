using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable Admin attestation of the physical cash returned for one frozen refund intent.</summary>
public sealed class AccountCashRefundEvidence : Entity
{
    public Guid IntentId { get; set; }
    public long ExactRefundAmountMinor { get; set; }
    public long RefundAdjustmentMinor { get; set; }
    public long CashReturnedMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public UserRole ActorRole { get; set; }
    public string TillReference { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; }
    public AccountCashRefundIntent? Intent { get; set; }
}
