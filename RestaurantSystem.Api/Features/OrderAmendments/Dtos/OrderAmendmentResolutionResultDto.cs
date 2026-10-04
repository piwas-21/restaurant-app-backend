namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

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
    IReadOnlyList<OrderAmendmentRefundLegResultDto> RefundLegs);

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
