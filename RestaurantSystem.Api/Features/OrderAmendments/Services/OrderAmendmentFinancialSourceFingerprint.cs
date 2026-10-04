using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentFinancialSourceFingerprint
{
    internal static string Create(OrderAmendmentFinancialSourceState state)
    {
        var (source, amendment, sourceAmendments, attempts, journals, reversals,
            authorizedRefundMinorByPayment, credits, loyaltyTransactions, currency) = state;
        var snapshot = new SourceSnapshot(
            source.Id, source.ServiceSessionId, source.Type, currency,
            source.SubTotal, source.Tax, source.DeliveryFee, source.Discount,
            source.DiscountPercentage, source.Tip, source.Total, source.TotalPaid,
            source.RemainingAmount, source.BillingCreditAmount, source.PaymentStatus,
            source.FidelityPointsEarned, source.FidelityPointsRedeemed,
            source.FidelityPointsDiscount, source.CustomerDiscountAmount,
            source.CustomerDiscountRuleId, source.PromoCode, source.HasUserLimitDiscount,
            source.UserLimitAmount, source.ExternalReference?.Id,
            source.ServiceSession?.Status, source.ServiceSession?.Currency,
            source.ServiceSession?.BillingAllocationVersion,
            source.Items.OrderBy(value => value.Id).Select(Item).ToArray(),
            source.Payments.OrderBy(value => value.Id).Select(Payment).ToArray(),
            sourceAmendments.OrderBy(value => value.Id).Select(Amendment).ToArray(),
            attempts.OrderBy(value => value.Id).Select(Attempt).ToArray(),
            journals.OrderBy(value => value.AttemptId).Select(Journal).ToArray(),
            reversals.OrderBy(value => value.Id).Select(Reversal).ToArray(),
            authorizedRefundMinorByPayment.OrderBy(value => value.Key)
                .Select(value => new AuthorizedRefund(value.Key, value.Value)).ToArray(),
            credits.OrderBy(value => value.Id).Select(Credit).ToArray(),
            loyaltyTransactions.OrderBy(value => value.Id).Select(Loyalty).ToArray(),
            amendment.Id, amendment.SourceOrderId, amendment.ServiceSessionId,
            amendment.ActorUserId, amendment.ActorRole, amendment.State,
            amendment.SupplementOrderId, amendment.ChangesJson,
            amendment.FinancialResolutionJson, amendment.SourceSnapshotJson,
            amendment.SupplementSnapshotJson);
        return OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(snapshot));
    }

    private static ItemSnapshot Item(OrderItem value) => new(
        value.Id, value.ProductId, value.ProductVariationId, value.MenuId,
        value.ParentOrderItemId, value.Kind, value.Quantity, value.UnitPrice,
        value.ItemTotal, value.SpecialInstructions, value.IngredientQuantitiesJson);

    private static PaymentSnapshot Payment(OrderPayment value) => new(
        value.Id, value.PaymentMethod, value.Amount, value.Currency, value.Status,
        value.IsRefunded, value.RefundedAmount, value.RefundDate, value.PaymentDate,
        value.PaymentGateway, value.TransactionId);

    private static AmendmentSnapshot Amendment(OrderAmendment value) => new(
        value.Id, value.SourceOrderId, value.ServiceSessionId, value.ActorUserId,
        value.ActorRole, value.State, value.SupplementOrderId, value.ChangesJson,
        value.FinancialResolutionJson, value.SourceSnapshotJson, value.SupplementSnapshotJson);

    private static AttemptSnapshot Attempt(AccountPaymentAttempt value) => new(
        value.Id, value.ServiceSessionId, value.OperationId, value.ActorId, value.ActorKind,
        value.Mode, value.State, value.PaymentMethod, value.AmountMinor, value.Currency,
        value.PayloadHash, OrderAmendmentJson.Hash(value.SnapshotJson), value.ProviderSessionId,
        value.ProviderChargeId, value.ProviderAccountId, value.EqualSharePlanId,
        value.EqualShareOrdinal,
        value.Allocations.OrderBy(allocation => allocation.Id).Select(Allocation).ToArray());

    private static AllocationSnapshot Allocation(AccountPaymentAllocation value) => new(
        value.Id, value.OrderId, value.OrderItemId, value.OrderPaymentId,
        value.StartOrdinal, value.UnitCount, value.MinorPerUnit, value.AmountMinor);

    private static JournalSnapshot Journal(AccountCheckoutJournal value) => new(
        value.AttemptId, value.AmountMinor, value.Currency, value.ProviderAccountId,
        value.ProviderLiveMode, value.ProviderSessionId, value.ProviderIntentId,
        value.ProviderChargeId, value.ProviderCapturedMinor, value.ReconciliationRequired);

    private static ReversalSnapshot Reversal(AccountPaymentAllocationReversal value) => new(
        value.Id, value.AllocationId, value.RefundLegId, value.OrderId,
        value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit,
        value.AmountMinor, value.Currency, value.ActorUserId, value.ActorRole);

    private static CreditSnapshot Credit(OrderBillingCredit value) => new(
        value.Id, value.SourceOrderId, value.AmendmentId, value.AmountMinor,
        value.Currency, value.ActorUserId, value.ActorRole);

    private static LoyaltySnapshot Loyalty(FidelityPointsTransaction value) => new(
        value.Id, value.UserId, value.OrderId, value.TransactionType,
        value.Points, value.OrderTotal, value.ExpiresAt);

    private sealed record SourceSnapshot(
        Guid OrderId, Guid? ServiceSessionId, OrderType OrderType, string Currency,
        decimal SubTotal, decimal Tax, decimal DeliveryFee, decimal Discount,
        decimal DiscountPercentage, decimal Tip, decimal Total, decimal TotalPaid,
        decimal RemainingAmount, decimal BillingCreditAmount, PaymentStatus PaymentStatus,
        int FidelityPointsEarned, int FidelityPointsRedeemed, decimal FidelityPointsDiscount,
        decimal CustomerDiscountAmount, Guid? CustomerDiscountRuleId, string? PromoCode,
        bool HasUserLimitDiscount, decimal UserLimitAmount, Guid? ExternalReferenceId,
        TableServiceSessionStatus? SessionStatus, string? SessionCurrency,
        int? BillingAllocationVersion, IReadOnlyList<ItemSnapshot> Items,
        IReadOnlyList<PaymentSnapshot> Payments, IReadOnlyList<AmendmentSnapshot> Amendments,
        IReadOnlyList<AttemptSnapshot> Attempts, IReadOnlyList<JournalSnapshot> Journals,
        IReadOnlyList<ReversalSnapshot> Reversals, IReadOnlyList<AuthorizedRefund> AuthorizedRefunds,
        IReadOnlyList<CreditSnapshot> Credits, IReadOnlyList<LoyaltySnapshot> LoyaltyTransactions,
        Guid AmendmentId, Guid AmendmentSourceOrderId, Guid? AmendmentServiceSessionId,
        Guid AmendmentActorUserId, string AmendmentActorRole, OrderAmendmentState AmendmentState,
        Guid? SupplementOrderId, string AmendmentChangesJson, string AmendmentFinancialJson,
        string AmendmentSourceSnapshotJson, string? AmendmentSupplementSnapshotJson);

    private sealed record ItemSnapshot(
        Guid Id, Guid? ProductId, Guid? ProductVariationId, Guid? MenuId,
        Guid? ParentOrderItemId, OrderItemKind? Kind, int Quantity, decimal UnitPrice,
        decimal ItemTotal, string? SpecialInstructions, string? IngredientQuantitiesJson);

    private sealed record PaymentSnapshot(
        Guid Id, PaymentMethod Method, decimal Amount, string? Currency, PaymentStatus Status,
        bool IsRefunded, decimal? RefundedAmount, DateTime? RefundDate, DateTime PaymentDate,
        string? PaymentGateway, string? TransactionId);

    private sealed record AmendmentSnapshot(
        Guid Id, Guid SourceOrderId, Guid? ServiceSessionId, Guid ActorUserId,
        string ActorRole, OrderAmendmentState State, Guid? SupplementOrderId,
        string ChangesJson, string FinancialJson, string SourceSnapshotJson,
        string? SupplementSnapshotJson);

    private sealed record AttemptSnapshot(
        Guid Id, Guid ServiceSessionId, Guid OperationId, Guid ActorId,
        AccountPaymentActorKind ActorKind, AccountPaymentMode Mode, AccountPaymentState State,
        PaymentMethod PaymentMethod, long AmountMinor, string Currency, string PayloadHash,
        string SnapshotHash, string? ProviderSessionId, string? ProviderChargeId,
        string? ProviderAccountId, Guid? EqualSharePlanId, int? EqualShareOrdinal,
        IReadOnlyList<AllocationSnapshot> Allocations);

    private sealed record AllocationSnapshot(
        Guid Id, Guid OrderId, Guid? OrderItemId, Guid? OrderPaymentId,
        int StartOrdinal, int UnitCount, long MinorPerUnit, long AmountMinor);

    private sealed record JournalSnapshot(
        Guid AttemptId, long AmountMinor, string Currency, string ProviderAccountId,
        bool ProviderLiveMode, string? ProviderSessionId, string? ProviderIntentId,
        string? ProviderChargeId, long ProviderCapturedMinor, bool ReconciliationRequired);

    private sealed record ReversalSnapshot(
        Guid Id, Guid AllocationId, Guid RefundLegId, Guid OrderId, Guid? OrderItemId,
        int StartOrdinal, int UnitCount, long MinorPerUnit, long AmountMinor,
        string Currency, Guid ActorUserId, string ActorRole);

    private sealed record AuthorizedRefund(Guid PaymentId, long AmountMinor);

    private sealed record CreditSnapshot(
        Guid Id, Guid SourceOrderId, Guid AmendmentId, long AmountMinor,
        string Currency, Guid ActorUserId, string ActorRole);

    private sealed record LoyaltySnapshot(
        Guid Id, Guid UserId, Guid? OrderId, TransactionType TransactionType,
        int Points, decimal? OrderTotal, DateTime? ExpiresAt);
}
