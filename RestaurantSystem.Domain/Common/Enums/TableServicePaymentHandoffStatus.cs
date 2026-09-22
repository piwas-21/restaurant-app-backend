namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Lifecycle of a cashier collection request for one service session.</summary>
public enum TableServicePaymentHandoffStatus
{
    Requested = 1,
    Resolved = 2,
    Cancelled = 3
}
