namespace RestaurantSystem.Domain.Entities;

public partial class Order
{
    // Legacy rows are released by default; staff counter rows may be held explicitly.
    public bool IsKitchenReleased { get; set; } = true;
    public DateTime? KitchenReleasedAt { get; set; }
    public string? KitchenReleasedBy { get; set; }
    public int Version { get; set; } = 1;
}
