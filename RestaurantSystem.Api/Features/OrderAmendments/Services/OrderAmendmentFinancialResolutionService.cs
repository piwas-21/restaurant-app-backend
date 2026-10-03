using System.Numerics;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed class OrderAmendmentFinancialResolutionService : IOrderAmendmentFinancialResolution
{
    private const int MinorUnitsPerMajor = 100;
    private readonly IOrderDisplayCurrencyResolver _currencyResolver;

    public OrderAmendmentFinancialResolutionService(IOrderDisplayCurrencyResolver currencyResolver) =>
        _currencyResolver = currencyResolver;

    public Task<OrderAmendmentFinancialPreviewDto> PreviewAsync(
        Order source,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        Order? supplement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (supplement is null && changes.All(change => change.Kind == OrderAmendmentChangeKind.InstructionChange))
        {
            return Task.FromResult(new OrderAmendmentFinancialPreviewDto(
                null, 0, 0, 0, 0,
                OrderAmendmentFinancialResolutionStatus.NotRequired,
                OrderAmendmentCreditState.None,
                OrderAmendmentLoyaltyState.None,
                OrderAmendmentRefundState.None));
        }

        var currencyLabel = source.ServiceSession?.Currency ?? _currencyResolver.Resolve(source);
        var currency = CheckoutAmount.From(1m, currencyLabel).Currency.ToUpperInvariant();
        var removed = CalculateRemovedMinor(source, changes);
        var added = supplement is null
            ? 0
            : ToMinor(Math.Max(0m, supplement.Total
                - Math.Max(0m, supplement.Tip)
                - Math.Max(0m, supplement.DeliveryFee)));
        var captured = source.Payments.Where(payment => payment.Status.IsCaptured()
            && payment.Amount - (payment.RefundedAmount ?? 0m) > 0m).ToList();
        var hasCredit = removed > 0;
        var hasLoyalty = hasCredit
            && (source.FidelityPointsEarned > 0 || source.FidelityPointsRedeemed > 0);
        var refundState = OrderAmendmentRefundState.None;
        if (hasCredit && captured.Count > 0)
            refundState = captured.Any(TenderCustody.IsHeldByGateway)
                ? OrderAmendmentRefundState.GatewayRefundRequired
                : OrderAmendmentRefundState.PendingTillRefund;
        var creditState = OrderAmendmentCreditState.None;
        if (hasCredit)
            creditState = captured.Count == 0
                ? OrderAmendmentCreditState.BalanceReduction
                : OrderAmendmentCreditState.PendingAllocationReview;
        var loyaltyState = hasLoyalty
            ? OrderAmendmentLoyaltyState.PendingReview
            : OrderAmendmentLoyaltyState.None;
        var status = !hasCredit
            ? OrderAmendmentFinancialResolutionStatus.NotRequired
            : OrderAmendmentFinancialResolutionStatus.Pending;

        return Task.FromResult(new OrderAmendmentFinancialPreviewDto(
            currency, added, removed, checked(added - removed), removed,
            status, creditState, loyaltyState, refundState));
    }

    public Task StageAsync(
        OrderAmendment amendment,
        Order source,
        OrderAmendmentFinancialPreviewDto preview,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(preview);
        return Task.CompletedTask;
    }

    private static long CalculateRemovedMinor(
        Order source, IReadOnlyList<OrderAmendmentChangeSnapshot> changes)
    {
        var removals = changes
            .Where(change => change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)
            .ToList();
        if (removals.Count == 0)
            return 0;

        var lines = source.Items.Where(item => !item.ParentOrderItemId.HasValue).ToList();
        var weights = lines.Select(item => Math.Max(0, ToMinor(item.ItemTotal))).ToArray();
        var foodTotal = Math.Max(0, ToMinor(
            source.Total - Math.Max(0m, source.Tip) - Math.Max(0m, source.DeliveryFee)));
        var lineShares = Allocate(foodTotal, weights);
        var lineIndexes = lines.Select((line, index) => (line.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        long removed = 0;

        foreach (var change in removals)
        {
            if (!lineIndexes.TryGetValue(change.OrderItemId, out var index))
                continue;
            var sourceLine = lines[index];
            var quantity = change.Quantity;
            if (quantity >= sourceLine.Quantity)
            {
                removed = checked(removed + lineShares[index]);
                continue;
            }

            var perUnit = lineShares[index] / sourceLine.Quantity;
            var remainder = (int)(lineShares[index] % sourceLine.Quantity);
            var end = (long)change.StartOrdinal + quantity - 1;
            var highValueUnits = Math.Max(0L,
                Math.Min(end, remainder) - change.StartOrdinal + 1);
            removed = checked(removed + (perUnit * quantity) + highValueUnits);
        }

        return removed;
    }

    private static long[] Allocate(long total, long[] weights)
    {
        if (weights.Length == 0 || total <= 0)
            return new long[weights.Length];
        var denominator = weights.Aggregate(BigInteger.Zero, (sum, weight) => sum + Math.Max(0, weight));
        if (denominator == BigInteger.Zero)
            return new long[weights.Length];

        var allocations = new long[weights.Length];
        var remainders = new BigInteger[weights.Length];
        long allocated = 0;
        for (var index = 0; index < weights.Length; index++)
        {
            var numerator = new BigInteger(total) * Math.Max(0, weights[index]);
            allocations[index] = (long)(numerator / denominator);
            remainders[index] = numerator % denominator;
            allocated = checked(allocated + allocations[index]);
        }

        var remaining = total - allocated;
        foreach (var index in Enumerable.Range(0, weights.Length)
                     .OrderByDescending(index => remainders[index]).ThenBy(index => index)
                     .Take((int)Math.Min(remaining, weights.Length)))
        {
            allocations[index]++;
        }

        return allocations;
    }

    private static long ToMinor(decimal amount) => checked(decimal.ToInt64(
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero) * MinorUnitsPerMajor));
}
