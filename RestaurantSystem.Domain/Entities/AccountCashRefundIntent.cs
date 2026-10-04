using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable exact and physical refund terms bound to one cash receipt and refund leg.</summary>
public sealed class AccountCashRefundIntent : Entity
{
    public Guid RefundLegId { get; set; }
    public Guid OperationId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid CollectionReceiptId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long OriginalExactAmountMinor { get; set; }
    public long OriginalAdjustmentMinor { get; set; }
    public long OriginalDueAmountMinor { get; set; }
    public long PreviouslyRefundedExactMinor { get; set; }
    public long PreviouslyRefundedCashMinor { get; set; }
    public long ExactRefundAmountMinor { get; set; }
    public long RefundAdjustmentMinor { get; set; }
    public long CashRefundAmountMinor { get; set; }
    public long RetainedExactAmountMinor { get; set; }
    public long RetainedCashDueMinor { get; set; }
    public string PriorHistoryFingerprint { get; set; } = string.Empty;
    public OrderAmendmentRefundLeg? RefundLeg { get; set; }
    public OrderAmendmentResolutionOperation? Operation { get; set; }
    public AccountPaymentAttempt? Attempt { get; set; }
    public AccountCashCollectionReceipt? CollectionReceipt { get; set; }
    public AccountCashRefundEvidence? ReturnEvidence { get; set; }
}
