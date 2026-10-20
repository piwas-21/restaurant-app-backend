namespace RestaurantSystem.Domain.Common.Enums;

public enum CustomerOrderCancellationReason
{
    OrderChanged = 1,
    NotAwaitingCustomerApproval = 2,
    RestaurantManaged = 3,
    FinancialReview = 4,
    OrderingUnavailable = 5
}
