namespace RestaurantSystem.Domain.Common.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Refunded = 5,
    PartiallyRefunded = 6,
    Overpaid = 7,
    PartiallyPaid = 8
}
