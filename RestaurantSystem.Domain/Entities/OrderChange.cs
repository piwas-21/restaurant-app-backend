using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Durable change-feed entry for an order. Sequence is allocated from the database sequence, so
/// callers can use it as an ordering boundary without trusting an application clock.
/// </summary>
public class OrderChange
{
    public long Sequence { get; set; }
    public Guid OrderId { get; set; }
    public OrderChangeKind Kind { get; set; }
    public string? Reason { get; set; }

    public virtual Order Order { get; set; } = null!;
}
