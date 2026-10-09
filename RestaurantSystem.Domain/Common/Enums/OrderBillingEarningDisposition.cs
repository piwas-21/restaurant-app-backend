namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>What the order's earning boundary knew when it accepted the frozen billing snapshot.</summary>
public enum OrderBillingEarningDisposition
{
    Unevaluated = 1,
    Evaluated = 2,
    NoCustomerOwnerAtAcceptance = 3,
    LoyaltyModuleDisabledAtAcceptance = 4
}
