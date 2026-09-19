using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Settings.Dtos;

/// <summary>
/// What a guest may read about a type's confirmation behaviour without an account: the flow and
/// the review window the acknowledgement screen animates against. No admin-only fields.
/// </summary>
public class OrderTypeConfirmationPublicDto
{
    public OrderType OrderType { get; set; }
    public string ConfirmationFlow { get; set; } = OrderConfirmationFlows.Direct;
    public int ReviewWindowMinutes { get; set; } = OrderTypeConfiguration.DefaultReviewWindowMinutes;
}
