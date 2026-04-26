namespace RestaurantSystem.Domain.Common.Enums;
public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Preparing = 3,
    Ready = 4,
    OutForDelivery = 5,
    Delivered = 6,
    Completed = 7,
    Cancelled = 8,
    Refunded = 9,
    PendingApproval = 10
}
