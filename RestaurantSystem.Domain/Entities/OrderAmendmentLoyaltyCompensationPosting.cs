using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable posting receipt; movement ID stays opaque after personal ledger erasure.</summary>
public sealed class OrderAmendmentLoyaltyCompensationPosting : Entity
{
    public Guid CompensationId { get; set; }
    public Guid MovementTransactionId { get; set; }
    public int PointsDelta { get; set; }
    public DateTime PostedAt { get; set; }
}
