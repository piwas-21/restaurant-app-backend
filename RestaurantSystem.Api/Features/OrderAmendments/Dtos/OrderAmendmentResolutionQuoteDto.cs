namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentResolutionQuoteDto(
    Guid OrderId,
    Guid AmendmentId,
    Guid ClientOperationId,
    string QuoteHash,
    DateTime ExpiresAt,
    string Currency,
    long CreditMinor,
    long RefundMinor,
    long UnpaidWaivedMinor,
    IReadOnlyList<OrderAmendmentRefundLegQuoteDto> RefundLegs);

public sealed record OrderAmendmentRefundLegQuoteDto(
    Guid PaymentId,
    string PaymentMethod,
    string Custody,
    long AmountMinor,
    bool RequiresTillConfirmation,
    IReadOnlyList<RefundScopeQuoteDto> Scopes,
    CashRefundQuoteDto? CashRefund = null);

public sealed record CashRefundQuoteDto(
    string PolicyVersion,
    long OriginalExactAmountMinor,
    long OriginalDueAmountMinor,
    long PreviouslyRefundedExactMinor,
    long PreviouslyRefundedCashMinor,
    long ExactRefundAmountMinor,
    long RefundAdjustmentMinor,
    long CashRefundAmountMinor,
    long RetainedExactAmountMinor,
    long RetainedCashDueMinor);

public sealed record RefundScopeQuoteDto(
    Guid AllocationId,
    Guid? OrderItemId,
    int StartOrdinal,
    int UnitCount,
    long MinorPerUnit,
    long AmountMinor);
