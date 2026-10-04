using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionPlanner
{
    internal static OrderAmendmentResolutionPlan Build(OrderAmendmentResolutionPlanningInput input)
    {
        var (source, amendment, request, changes, attempts, checkoutJournals, reversals,
            priorAuthorizedRefundMinorByPayment, money, hasLoyaltyLedgerHistory) = input;
        var credit = ValidateSourceForResolution(source, amendment,
            priorAuthorizedRefundMinorByPayment, money, hasLoyaltyLedgerHistory);
        if (request.ExpectedOrderVersion != source.Version
            || request.ExpectedAccountRevision != source.ServiceSession?.AccountRevision
            || !string.Equals(request.Currency, money.Currency, StringComparison.Ordinal))
            throw new ConflictException("The order account changed. Review the amendment again.");
        var removals = OrderAmendmentRefundScopePlanner.RemovalRanges(changes);
        OrderAmendmentRefundScopePlanner.EnsureNoPriorRemovalRefund(removals, reversals);
        var legs = PlanAllocatedRefunds(source, attempts, checkoutJournals, reversals, removals, money);
        AddManualRefunds(source, request.ManualRefunds, attempts, legs, money);
        var refund = legs.Sum(value => value.AmountMinor);
        if (refund > credit)
            throw ReconciliationRequired("Captured refunds exceed the frozen food credit.");
        RequireEnoughRefundForRetainedBalance(source, credit, refund, money);
        return new OrderAmendmentResolutionPlan(money.Currency, credit, refund,
            checked(credit - refund), legs);
    }

    internal static long ValidateSourceForResolution(
        Order source, OrderAmendment amendment,
        IReadOnlyDictionary<Guid, long> priorAuthorizedRefundMinorByPayment,
        AccountMoney money, bool hasLoyaltyLedgerHistory)
    {
        if (source.ExternalReference is not null)
            throw ReconciliationRequired("Marketplace-held orders cannot use local amendment settlement.");
        if (source.Tax != 0 || source.FidelityPointsEarned != 0
            || source.FidelityPointsRedeemed != 0 || source.FidelityPointsDiscount != 0
            || hasLoyaltyLedgerHistory)
            throw ReconciliationRequired("Tax or loyalty effects need a frozen compensation review.");
        if (source.Status is OrderStatus.Cancelled or OrderStatus.Refunded
            || source.PaymentStatus == PaymentStatus.Refunded)
            throw ReconciliationRequired("Existing refund activity requires reconciliation.");
        foreach (var payment in source.Payments)
            RequireAuthorizedSourceRefund(payment, priorAuthorizedRefundMinorByPayment, money);
        if (amendment.State != OrderAmendmentState.Committed
            || amendment.SourceOrderId != source.Id
            || amendment.ServiceSessionId != source.ServiceSessionId)
            throw new ConflictException("Only a committed amendment can be resolved.");
        if (source.ServiceSession is not null
            && source.ServiceSession.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("The table account is no longer open for resolution.");
        return ReadCredit(amendment, money.Currency);
    }

    private static void RequireAuthorizedSourceRefund(OrderPayment payment,
        IReadOnlyDictionary<Guid, long> priorAuthorizedRefundMinorByPayment, AccountMoney money)
    {
        var actualRefund = money.ToMinor(payment.RefundedAmount
            ?? (payment.IsRefunded || payment.Status == PaymentStatus.Refunded ? payment.Amount : 0m));
        var priorAuthorized = priorAuthorizedRefundMinorByPayment.GetValueOrDefault(payment.Id);
        if (actualRefund != priorAuthorized
            || actualRefund > 0 && (!payment.Status.IsCaptured()
                || actualRefund < money.ToMinor(payment.Amount)
                    && payment.Status != PaymentStatus.PartiallyRefunded
                || actualRefund == money.ToMinor(payment.Amount)
                    && payment.Status != PaymentStatus.Refunded))
            throw ReconciliationRequired("Existing refund activity lacks exact resolved amendment evidence.");
    }

    private static long ReadCredit(OrderAmendment amendment, string currency)
    {
        var financial = OrderAmendmentFinancialGuard.ReadValidSnapshot(amendment.FinancialResolutionJson);
        if (financial.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.Pending
            || financial.PotentialCreditMinor <= 0 || financial.Currency != currency
            || financial.CreditState != OrderAmendmentCreditState.PendingAllocationReview)
            throw new ConflictException("This amendment has no paid food credit awaiting resolution.");
        return financial.PotentialCreditMinor;
    }

    internal static List<OrderAmendmentRefundLegPlan> PlanAllocatedRefunds(
        Order source, IReadOnlyList<AccountPaymentAttempt> attempts,
        IReadOnlyList<AccountCheckoutJournal> checkoutJournals,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        IReadOnlyList<OrderAmendmentRefundRange> removals, AccountMoney money)
    {
        var result = new List<OrderAmendmentRefundLegPlan>();
        var covered = new Dictionary<Guid, List<OrderAmendmentRefundRange>>();
        foreach (var attempt in attempts.Where(value => value.ServiceSessionId == source.ServiceSessionId))
        {
            if (attempt.State != AccountPaymentState.Captured && attempt.State.HoldsReservation())
                RejectHeldAllocation(source.Id, attempt, removals);
            if (attempt.State != AccountPaymentState.Captured)
                continue;
            var allocations = attempt.Allocations.Where(value => value.OrderId == source.Id).ToArray();
            var slices = allocations.SelectMany(allocation => OrderAmendmentRefundScopePlanner.Intersections(
                allocation, reversals, removals, covered)).ToArray();
            if (slices.Length == 0)
                continue;
            AddAllocatedLeg(source, attempt, allocations, slices, checkoutJournals, money, result);
        }
        return result;
    }

    private static void RejectHeldAllocation(
        Guid orderId, AccountPaymentAttempt attempt, IReadOnlyList<OrderAmendmentRefundRange> removals)
    {
        if (attempt.Allocations.Any(allocation => allocation.OrderId == orderId
                && (allocation.OrderItemId is null
                || removals.Any(range => range.ItemId == allocation.OrderItemId
                    && allocation.StartOrdinal < range.End
                    && range.Start < (long)allocation.StartOrdinal + allocation.UnitCount))))
            throw new ConflictException("A reserved or processing payment still holds the removed item scope.");
    }

    private static void AddAllocatedLeg(
        Order source, AccountPaymentAttempt attempt, IReadOnlyList<AccountPaymentAllocation> allocations,
        IReadOnlyList<OrderAmendmentRefundScope> slices,
        IReadOnlyList<AccountCheckoutJournal> checkoutJournals, AccountMoney money,
        List<OrderAmendmentRefundLegPlan> legs)
    {
        var paymentIds = slices.Select(value => allocations.Single(allocation => allocation.Id == value.AllocationId)
            .OrderPaymentId).Distinct().ToArray();
        if (paymentIds.Length != 1 || paymentIds[0] is not Guid paymentId)
            throw ReconciliationRequired("A captured refund scope lacks one exact source tender.");
        var payment = source.Payments.SingleOrDefault(value => value.Id == paymentId);
        if (payment is null || !payment.Status.IsCaptured())
            throw ReconciliationRequired("A captured allocation has conflicting tender custody.");
        if (attempt.Currency != money.Currency || allocations.Any(value => value.OrderPaymentId != paymentId
                || value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount)))
            throw ReconciliationRequired("A captured refund allocation does not match its tender.");
        var custody = ResolveCustody(payment, attempt, checkoutJournals, money);
        var amount = slices.Sum(value => value.AmountMinor);
        var journal = checkoutJournals.SingleOrDefault(value => value.AttemptId == attempt.Id);
        legs.Add(new OrderAmendmentRefundLegPlan(payment, attempt.Id, custody, amount, slices,
            journal?.ProviderAccountId, journal?.ProviderLiveMode,
            journal?.ProviderChargeId, journal?.ProviderIntentId));
    }

    private static OrderAmendmentRefundCustody ResolveCustody(
        OrderPayment payment, AccountPaymentAttempt attempt,
        IReadOnlyList<AccountCheckoutJournal> checkoutJournals, AccountMoney money)
    {
        if (payment.PaymentGateway is null)
        {
            if (payment.PaymentMethod is PaymentMethod.Cash or PaymentMethod.CreditCard)
                return OrderAmendmentRefundCustody.ManualTill;
            throw ReconciliationRequired("The allocated tender is not a supported local till payment.");
        }
        if (!string.Equals(payment.PaymentGateway, "Stripe", StringComparison.OrdinalIgnoreCase)
            || payment.PaymentMethod != PaymentMethod.OnlinePayment)
            throw ReconciliationRequired("The captured tender has an unsupported payment provider.");
        var journal = checkoutJournals.SingleOrDefault(value => value.AttemptId == attempt.Id);
        if (journal is null || journal.ReconciliationRequired || journal.ProviderCapturedMinor != attempt.AmountMinor
            || journal.Currency != money.Currency || string.IsNullOrWhiteSpace(journal.ProviderIntentId)
            || string.IsNullOrWhiteSpace(journal.ProviderChargeId)
            || journal.ProviderChargeId != attempt.ProviderChargeId
            || journal.ProviderChargeId != payment.TransactionId)
            throw ReconciliationRequired("The direct-charge capture lacks frozen provider context.");
        return OrderAmendmentRefundCustody.StripeDirect;
    }

    private static void AddManualRefunds(
        Order source, IReadOnlyList<ManualRefundSelectionRequest> selections,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        List<OrderAmendmentRefundLegPlan> legs, AccountMoney money)
    {
        var paymentIds = new HashSet<Guid>();
        foreach (var selection in selections)
        {
            if (!paymentIds.Add(selection.PaymentId) || selection.AmountMinor <= 0)
                throw new BadRequestException("Manual refund selections must be unique positive amounts.");
            if (legs.Any(value => value.Payment.Id == selection.PaymentId))
                throw new BadRequestException("An allocated tender refund amount is calculated from its frozen item scopes.");
            var payment = source.Payments.SingleOrDefault(value => value.Id == selection.PaymentId);
            if (payment is null || !payment.Status.IsCaptured() || payment.PaymentGateway is not null
                || payment.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard)
                || attempts.SelectMany(value => value.Allocations)
                    .Any(value => value.OrderPaymentId == payment.Id))
                throw ReconciliationRequired("Only an unallocated local cash or till-card tender can be explicitly selected.");
            var remaining = checked(money.ToMinor(payment.Amount)
                - money.ToMinor(payment.RefundedAmount ?? 0m));
            if (remaining < selection.AmountMinor)
                throw new BadRequestException("The selected till refund exceeds its captured payment.");
            legs.Add(new OrderAmendmentRefundLegPlan(payment, null,
                OrderAmendmentRefundCustody.ManualTill, selection.AmountMinor, [], null, null, null, null));
        }
    }

    private static void RequireEnoughRefundForRetainedBalance(
        Order source, long credit, long refund, AccountMoney money)
    {
        var captured = source.Payments.Where(value => value.Status.IsCaptured()).Sum(value =>
            money.ToMinor(value.Amount - (value.RefundedAmount ?? 0m)));
        if (money.ToMinor(source.TotalPaid) > captured)
            throw ReconciliationRequired("The source payment summary exceeds captured tender evidence.");
        var newPayable = money.ToMinor(source.PayableTotal) - credit;
        if (newPayable < 0)
            throw ReconciliationRequired("The food credit exceeds the retained order charge.");
        var minimumRefund = Math.Max(0, captured - newPayable);
        if (refund < minimumRefund)
            throw ReconciliationRequired("Select and attest the unallocated local tender amount needed to keep the retained balance exact.");
    }

    private static ConflictException ReconciliationRequired(string message) => new(message);

}
