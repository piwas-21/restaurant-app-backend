using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable provider request identity; observations are stored separately and append-only.</summary>
public sealed class OrderAmendmentRefundAttempt : Entity
{
    public Guid RefundLegId { get; set; }
    public int Sequence { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}
