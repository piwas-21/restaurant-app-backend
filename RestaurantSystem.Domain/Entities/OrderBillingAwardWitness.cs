using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public enum OrderBillingAwardOutcome
{
    Awarded = 1,
    EvaluatedZero = 2,
    FullySuppressed = 3
}

/// <summary>Durable result of applying a native order's frozen earning allocation.</summary>
/// <remarks>
/// The earned transaction ID is opaque lineage only. This journal intentionally has no user ID
/// and no foreign key to the personal fidelity transaction row so its accepted facts survive erasure.
/// </remarks>
public sealed class OrderBillingAwardWitness : Entity
{
    public Guid OrderId { get; set; }
    public Guid OwnerLinkId { get; set; }
    public OrderBillingAwardOutcome Outcome { get; set; }
    public int CandidatePoints { get; set; }
    public int AppliedPoints { get; set; }
    public int SuppressedPoints { get; set; }
    public Guid? EarnedTransactionId { get; set; }
}
