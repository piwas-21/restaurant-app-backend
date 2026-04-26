using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OrderStatusHistory : Entity
{
    public Guid OrderId { get; set; }
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = null!;

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
}
