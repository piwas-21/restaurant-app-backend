using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Append-only till attestation or canonical Stripe refund response observation.</summary>
public sealed class OrderAmendmentRefundEvidence : Entity
{
    public Guid RefundLegId { get; set; }
    public Guid? RefundAttemptId { get; set; }
    public int Sequence { get; set; }
    public OrderAmendmentRefundEvidenceKind Kind { get; set; }
    public OrderAmendmentRefundLegState State { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; }
    public string? TillReference { get; set; }
    public string? ProviderRefundId { get; set; }
    public string? ProviderRefundStatus { get; set; }
    public string? ProviderChargeId { get; set; }
    public string? ProviderIntentId { get; set; }
    public string? ProviderAccountId { get; set; }
    public bool? ProviderLiveMode { get; set; }
    public string? FailureCode { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}
