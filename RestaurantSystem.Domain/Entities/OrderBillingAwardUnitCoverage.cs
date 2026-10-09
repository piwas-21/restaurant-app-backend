using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable proof that a unit's frozen points were included in the applied award.</summary>
public sealed class OrderBillingAwardUnitCoverage : Entity
{
    public Guid OrderId { get; set; }
    public Guid AwardWitnessId { get; set; }
    public Guid SnapshotUnitId { get; set; }
    public int EligibleEarnedPoints { get; set; }
}
