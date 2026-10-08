using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>PII-free proof that an unknown earning evaluation was retired by removing every source unit.</summary>
public sealed class OrderBillingEarningRetirement : Entity
{
    public Guid OrderId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid AmendmentId { get; set; }
    public int RetiredUnitCount { get; set; }
}
