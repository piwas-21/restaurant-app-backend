using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Frozen refund amount and custody for one source tender within a resolution.</summary>
public sealed class OrderAmendmentRefundLeg : Entity
{
    public Guid OperationId { get; set; }
    public Guid SourcePaymentId { get; set; }
    public Guid? AccountPaymentAttemptId { get; set; }
    public OrderAmendmentRefundCustody Custody { get; set; }
    public OrderAmendmentRefundLegState State { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string FrozenScopesJson { get; set; } = "[]";
    public string? ProviderAccountId { get; set; }
    public bool? ProviderLiveMode { get; set; }
    public string? ProviderChargeId { get; set; }
    public string? ProviderIntentId { get; set; }
    public string? ManualTillReference { get; set; }
    public string? FailureCode { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public ICollection<OrderAmendmentRefundAttempt> Attempts { get; set; } = [];
}
