namespace RestaurantSystem.Domain.Common.Enums;

public enum OrderAmendmentRefundLegState
{
    Processing = 1,
    Pending = 2,
    Failed = 3,
    ReconciliationRequired = 4,
    Succeeded = 5
}
