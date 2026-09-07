using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Settings.Dtos;

public class OrderTypeConfigurationDto
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this type is refused while the restaurant is outside its working hours (#448).
    /// Defaults keep the historical behaviour: DineIn true, Takeaway/Delivery false.
    /// </summary>
    public bool EnforceOpeningHours { get; set; }
}

public class UpdateOrderTypeConfigurationDto
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Nullable on purpose: omitted means "leave unchanged". The shipped frontend sends only
    /// <c>orderType</c> + <c>isEnabled</c>; a required bool would switch the hours gate off on
    /// every such save (the under-posting trap: absent must not mean deliberately zero).
    /// </summary>
    public bool? EnforceOpeningHours { get; set; }
}
