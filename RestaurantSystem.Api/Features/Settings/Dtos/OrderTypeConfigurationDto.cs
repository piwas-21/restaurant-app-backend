using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

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

    /// <summary>
    /// The order-confirmation flow for this type: <c>direct</c> (kitchen starts immediately,
    /// historical behaviour) or <c>acknowledge</c> (guest is told the order is received and under
    /// review for a bounded window; a cashier approves it explicitly). See
    /// <c>OrderConfirmationFlows</c>.
    /// </summary>
    public string ConfirmationFlow { get; set; } = OrderConfirmationFlows.Direct;

    /// <summary>
    /// The promised review window in minutes for the <c>acknowledge</c> flow (guest-facing).
    /// </summary>
    public int ReviewWindowMinutes { get; set; } = OrderTypeConfiguration.DefaultReviewWindowMinutes;
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

    /// <summary>
    /// Nullable for the same under-posting reason: an older admin client that has never heard of
    /// flows must not flip a tenant that opted into <c>acknowledge</c> back to <c>direct</c>.
    /// </summary>
    public string? ConfirmationFlow { get; set; }

    /// <summary>
    /// Nullable: omitted leaves the stored window. Only read when the (also optional) flow is
    /// <c>acknowledge</c>.
    /// </summary>
    public int? ReviewWindowMinutes { get; set; }
}
