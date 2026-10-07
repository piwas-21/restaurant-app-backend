namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public enum OrderAmendmentLoyaltyOperationStatus
{
    None = 0,
    AwaitingAwardSuppression = 1,
    PendingSettlement = 2,
    HeldShortfall = 3,
    Reserved = 4,
    ReleasedAfterNoRefund = 5,
    OwnerUnavailable = 6,
    Resolved = 7
}

public sealed record OrderAmendmentResolutionResultDto(
    Guid OperationId,
    Guid ClientOperationId,
    Guid AmendmentId,
    Guid SourceOrderId,
    string State,
    string Currency,
    long CreditMinor,
    long RefundMinor,
    long UnpaidWaivedMinor,
    DateTime StartedAt,
    DateTime? ResolvedAt,
    IReadOnlyList<OrderAmendmentRefundLegResultDto> RefundLegs,
    OrderAmendmentLoyaltyResultDto? Loyalty = null);

public sealed record OrderAmendmentLoyaltyResultDto(
    OrderAmendmentLoyaltyOperationStatus State,
    bool AwardPending,
    int CandidatePoints,
    int AppliedAwardPoints,
    int SuppressedPoints,
    int EarnedClawbackPoints,
    int RedemptionRestorationPoints,
    int PostedClawbackPoints,
    int PostedRestorationPoints,
    long? AvailablePointsBeforeClawback,
    int? ClawbackShortfallPoints);

public sealed record OrderAmendmentRefundLegResultDto(
    Guid PaymentId,
    string Custody,
    string State,
    long AmountMinor,
    DateTime? ResolvedAt,
    ManualTillConfirmationResultDto? TillConfirmation = null,
    CashRefundQuoteDto? CashRefund = null,
    CashReturnEvidenceDto? CashReturn = null);

public sealed record ManualTillConfirmationResultDto(string TillReference, DateTime ConfirmedAt);

public sealed record CashReturnEvidenceDto(
    long ExactRefundAmountMinor,
    long RefundAdjustmentMinor,
    long CashReturnedMinor,
    DateTime ConfirmedAt);
