using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OrderTypeConfiguration : Entity
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }
    public int DisplayOrder { get; set; }
}
