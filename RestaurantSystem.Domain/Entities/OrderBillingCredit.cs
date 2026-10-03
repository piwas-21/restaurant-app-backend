using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One immutable food-charge reduction from one committed amendment.</summary>
public sealed class OrderBillingCredit : Entity
{
    public Guid SourceOrderId { get; set; }
    public Guid AmendmentId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
}
