using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable reviewed scope, actor replay key and provider outcome for one visit contribution.</summary>
public sealed class AccountPaymentAttempt : Entity
{
    public Guid ServiceSessionId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorId { get; set; }
    public AccountPaymentActorKind ActorKind { get; set; }
    public AccountPaymentMode Mode { get; set; }
    public AccountPaymentState State { get; set; } = AccountPaymentState.Quoted;
    public PaymentMethod PaymentMethod { get; set; }
    public int Version { get; set; } = 1;
    public long ExpectedAccountRevision { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime QuoteExpiresAt { get; set; }
    public DateTime? ReservedAt { get; set; }
    public DateTime? ReservationExpiresAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ProviderSessionId { get; set; }
    public string? ProviderChargeId { get; set; }
    public string? ProviderAccountId { get; set; }
    public string? FailureCode { get; set; }
    public Guid? EqualSharePlanId { get; set; }
    public int? EqualShareOrdinal { get; set; }
    public TableServiceSession? ServiceSession { get; set; }
    public AccountEqualSharePlan? EqualSharePlan { get; set; }
    public ICollection<AccountPaymentAllocation> Allocations { get; set; } = [];
    public AccountCashCollectionReceipt? CashCollectionReceipt { get; set; }
}
