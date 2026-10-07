using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One exact frozen snapshot unit covered by a loyalty compensation plan.</summary>
public sealed class OrderAmendmentLoyaltyCompensationUnit : Entity
{
    public Guid CompensationId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid SnapshotUnitId { get; set; }
    public OrderAmendmentLoyaltyCompensationKind Kind { get; set; }
    public int Points { get; set; }
}
