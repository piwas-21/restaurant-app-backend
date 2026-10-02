namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public enum OrderAmendmentFinancialResolutionStatus
{
    NotRequired = 0,
    Pending = 1,
    Resolved = 2
}

public enum OrderAmendmentCreditState
{
    None = 0,
    BalanceReduction = 1,
    PendingAllocationReview = 2,
    Resolved = 3
}

public enum OrderAmendmentLoyaltyState
{
    None = 0,
    PendingReview = 1,
    Resolved = 2
}

public enum OrderAmendmentRefundState
{
    None = 0,
    PendingTillRefund = 1,
    GatewayRefundRequired = 2,
    CustodianReviewRequired = 3,
    Resolved = 4
}

/// <summary>
/// Deterministic quote values in the declared currency's minor units. Removed-unit value is the
/// source order's frozen net per-unit allocation; captured money remains unchanged for resolution.
/// </summary>
public sealed record OrderAmendmentFinancialPreviewDto(
    string? Currency,
    long AddedAmountMinor,
    long RemovedUnitValueMinor,
    long NetAccountDeltaMinor,
    long PotentialCreditMinor,
    OrderAmendmentFinancialResolutionStatus ResolutionStatus,
    OrderAmendmentCreditState CreditState,
    OrderAmendmentLoyaltyState LoyaltyState,
    OrderAmendmentRefundState RefundState);
