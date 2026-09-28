namespace RestaurantSystem.Domain.Entities;

/// <summary>Monotonic per-tenant, per-day order-number watermark.</summary>
public class OrderNumberSequence
{
    public DateOnly Day { get; set; }
    public long LastSequence { get; set; }
}
