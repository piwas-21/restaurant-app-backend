using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable pre-award exclusion for one removed snapshot unit.</summary>
/// <remarks>
/// This records only the accepted per-unit points and amendment identity. It does not change the
/// original billing preview or snapshot and contains no user identity or fidelity transaction FK.
/// Zero-point units need no row because excluding them cannot change the frozen award total.
/// </remarks>
public sealed class OrderBillingUnitAwardSuppression : Entity
{
    public Guid OrderId { get; set; }
    public Guid SnapshotUnitId { get; set; }
    public Guid AmendmentId { get; set; }
    public int SuppressedEarnedPoints { get; set; }
}
