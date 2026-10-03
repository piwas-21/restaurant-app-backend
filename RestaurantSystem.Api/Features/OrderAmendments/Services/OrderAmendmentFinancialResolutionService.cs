using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed class OrderAmendmentFinancialResolutionService : IOrderAmendmentFinancialResolution
{
    private const int MinorUnitsPerMajor = 100;
    private readonly IOrderDisplayCurrencyResolver _currencyResolver;

    private readonly IOrderBillingAdjustmentWriter _billing;

    public OrderAmendmentFinancialResolutionService(
        IOrderDisplayCurrencyResolver currencyResolver, IOrderBillingAdjustmentWriter billing)
    {
        _currencyResolver = currencyResolver;
        _billing = billing;
    }

    public Task<OrderAmendmentFinancialPreviewDto> PreviewAsync(
        Order source,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        Order? supplement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsInstructionOnly(changes, supplement))
            return Task.FromResult(CreateInstructionOnlyPreview());

        var currencyLabel = source.ServiceSession?.Currency ?? _currencyResolver.Resolve(source);
        var currency = CheckoutAmount.From(1m, currencyLabel).Currency.ToUpperInvariant();
        var removed = CalculateRemovedMinor(source, changes, new AccountMoney(currency));
        var added = supplement is null
            ? 0
            : ToMinor(Math.Max(0m, supplement.Total
                - Math.Max(0m, supplement.Tip)
                - Math.Max(0m, supplement.DeliveryFee)));
        var captured = source.Payments.Where(payment => payment.Status.IsCaptured()
            && payment.Amount - (payment.RefundedAmount ?? 0m) > 0m).ToList();
        var hasUnattributedPaidAmount = captured.Count == 0 && ToMinor(source.TotalPaid) > 0;
        var hasCapturedOrUnattributedTender = captured.Count > 0 || hasUnattributedPaidAmount;
        var hasCredit = removed > 0;
        var hasLoyalty = hasCredit
            && (source.FidelityPointsEarned > 0 || source.FidelityPointsRedeemed > 0 || source.FidelityPointsDiscount > 0);
        var states = ResolveStates(hasCredit, hasLoyalty, hasCapturedOrUnattributedTender,
            source.Tax != 0, captured);

        return Task.FromResult(new OrderAmendmentFinancialPreviewDto(
            currency, added, removed, checked(added - removed), removed,
            states.Status, states.Credit, states.Loyalty, states.Refund));
    }

    private static bool IsInstructionOnly(
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes, Order? supplement) =>
        supplement is null && changes.All(change => change.Kind == OrderAmendmentChangeKind.InstructionChange);

    private static OrderAmendmentFinancialPreviewDto CreateInstructionOnlyPreview() => new(
        null, 0, 0, 0, 0,
        OrderAmendmentFinancialResolutionStatus.NotRequired,
        OrderAmendmentCreditState.None,
        OrderAmendmentLoyaltyState.None,
        OrderAmendmentRefundState.None);

    private static ResolutionStates ResolveStates(
        bool hasCredit,
        bool hasLoyalty,
        bool hasCapturedOrUnattributedTender,
        bool hasTax,
        IReadOnlyList<OrderPayment> captured)
    {
        var refund = DetermineRefundState(hasCredit, hasCapturedOrUnattributedTender, captured);
        var credit = DetermineCreditState(hasCredit, hasCapturedOrUnattributedTender, hasTax);
        var loyalty = hasLoyalty ? OrderAmendmentLoyaltyState.PendingReview : OrderAmendmentLoyaltyState.None;
        var status = hasCredit
            ? OrderAmendmentFinancialResolutionStatus.Pending
            : OrderAmendmentFinancialResolutionStatus.NotRequired;
        return new(status, credit, loyalty, refund);
    }

    private static OrderAmendmentRefundState DetermineRefundState(
        bool hasCredit,
        bool hasCapturedOrUnattributedTender,
        IReadOnlyList<OrderPayment> captured)
    {
        if (!hasCredit || !hasCapturedOrUnattributedTender)
            return OrderAmendmentRefundState.None;
        if (captured.Any(TenderCustody.IsHeldByGateway))
            return OrderAmendmentRefundState.GatewayRefundRequired;
        if (captured.Count > 0)
            return OrderAmendmentRefundState.PendingTillRefund;
        return OrderAmendmentRefundState.CustodianReviewRequired;
    }

    private static OrderAmendmentCreditState DetermineCreditState(
        bool hasCredit, bool hasCapturedOrUnattributedTender, bool hasTax)
    {
        if (!hasCredit)
            return OrderAmendmentCreditState.None;
        if (!hasCapturedOrUnattributedTender && !hasTax)
            return OrderAmendmentCreditState.BalanceReduction;
        return OrderAmendmentCreditState.PendingAllocationReview;
    }

    public async Task StageAsync(
        OrderAmendment amendment,
        Order source,
        OrderAmendmentFinancialPreviewDto preview,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var committed = preview.CreditState == OrderAmendmentCreditState.BalanceReduction
            && preview.LoyaltyState == OrderAmendmentLoyaltyState.None
            && preview.RefundState == OrderAmendmentRefundState.None
                ? preview with { ResolutionStatus = OrderAmendmentFinancialResolutionStatus.Resolved }
                : preview;
        if (committed.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Resolved
            && committed.CreditState == OrderAmendmentCreditState.BalanceReduction)
            await _billing.StageUnpaidCreditAsync(source, amendment, committed, cancellationToken);
        // Resolution is published only after the durable credit and effective balance are staged.
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(committed);
    }

    private static long CalculateRemovedMinor(
        Order source, IReadOnlyList<OrderAmendmentChangeSnapshot> changes, AccountMoney money)
    {
        var removals = changes.Where(change => change.Kind is
            OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace).ToArray();
        if (removals.Length == 0)
            return 0;
        var frozen = FrozenOrderChargeMath.Read(source, money);
        var lines = frozen.FoodLines.ToDictionary(line => line.Item.Id);
        long removed = 0;
        foreach (var change in removals)
        {
            if (!lines.TryGetValue(change.OrderItemId, out var line))
                throw new RestaurantSystem.Api.Common.Exceptions.ConflictException(
                    "The removed food line is absent from the frozen order.");
            removed = checked(removed + FrozenOrderChargeMath.RemovedUnits(
                line, change.StartOrdinal, change.Quantity));
        }
        return removed;
    }

    private static long ToMinor(decimal amount) => checked(decimal.ToInt64(
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero) * MinorUnitsPerMajor));
    private sealed record ResolutionStates(
        OrderAmendmentFinancialResolutionStatus Status,
        OrderAmendmentCreditState Credit,
        OrderAmendmentLoyaltyState Loyalty,
        OrderAmendmentRefundState Refund);
}
