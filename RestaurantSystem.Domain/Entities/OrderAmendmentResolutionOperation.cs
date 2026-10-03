using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Actor-bound durable request to settle one committed amendment's captured credit.</summary>
public sealed class OrderAmendmentResolutionOperation : Entity
{
    public Guid AmendmentId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public Guid ClientOperationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public int ExpectedOrderVersion { get; set; }
    public long? ExpectedAccountRevision { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long CreditMinor { get; set; }
    public long RefundMinor { get; set; }
    public long UnpaidWaivedMinor { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public OrderAmendmentResolutionOperationState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? FailureCode { get; set; }
    public ICollection<OrderAmendmentRefundLeg> Legs { get; set; } = [];
}
