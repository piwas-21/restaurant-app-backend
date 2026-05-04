using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Settings.Dtos;

public class OrderTypeConfigurationDto
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class UpdateOrderTypeConfigurationDto
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }
}
