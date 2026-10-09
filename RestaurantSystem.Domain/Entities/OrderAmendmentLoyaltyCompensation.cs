using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public enum OrderAmendmentLoyaltyCompensationKind
{
    EarnedClawback = 1,
    RedemptionRestoration = 2
}

/// <summary>Immutable point-effect plan keyed to one source ledger transaction and amendment.</summary>
/// <remarks>OwnerLinkId is the only owner reference; transaction IDs are opaque and have no FPT FK.</remarks>
public sealed class OrderAmendmentLoyaltyCompensation : Entity
{
    public Guid SourceOrderId { get; set; }
    public Guid AmendmentId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid OwnerLinkId { get; set; }
    public Guid OriginalTransactionId { get; set; }
    public Guid? AwardWitnessId { get; set; }
    public Guid OperationId { get; set; }
    public OrderAmendmentLoyaltyCompensationKind Kind { get; set; }
    public int OriginalTransactionPoints { get; set; }
    public int RequiredPoints { get; set; }
    public string PlanFingerprint { get; set; } = string.Empty;
}
