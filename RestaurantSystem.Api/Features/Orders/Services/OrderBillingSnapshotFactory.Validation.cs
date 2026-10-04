using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static partial class OrderBillingSnapshotFactory
{
    private static void ValidateCapacity(int maximumUnitRows)
    {
        if (maximumUnitRows is < 1 or > OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows)
            throw Reconciliation("The configured billing snapshot unit capacity is outside its supported range.");
    }

    private static void ValidateOrderIdentity(Order order)
    {
        if (order.Id == Guid.Empty)
            throw Reconciliation("A billing snapshot requires a persisted order.");
    }

    private static AccountMoney CreateMoney(string? currency)
    {
        try
        {
            return new AccountMoney(currency);
        }
        catch (BadRequestException)
        {
            throw Reconciliation("The accepted order currency is not supported for minor-unit snapshots.");
        }
    }

    private static OrderItem[] ValidateItems(
        Order order, OrderItem[] items, AccountMoney money)
    {
        if (items.Any(item => item.OrderId != order.Id || item.Id == Guid.Empty || item.Quantity <= 0)
            || items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw Reconciliation("The accepted order contains invalid or foreign item rows.");

        var byId = items.ToDictionary(item => item.Id);
        foreach (var item in items)
        {
            var amount = ToMinor(money, item.ItemTotal, false);
            if (item.ParentOrderItemId is null)
                continue;
            if (amount != 0 || !byId.ContainsKey(item.ParentOrderItemId.Value))
                throw Reconciliation("A bundle child must be attached to this order and carry no separate charge.");
            ValidateParentChain(item, byId);
        }

        return items.Where(item => item.ParentOrderItemId is null)
            .OrderBy(item => item.Id).ToArray();
    }

    private static void ValidateParentChain(OrderItem item, Dictionary<Guid, OrderItem> items)
    {
        var visited = new HashSet<Guid> { item.Id };
        var parentId = item.ParentOrderItemId;
        while (parentId.HasValue)
        {
            if (!items.TryGetValue(parentId.Value, out var parent) || !visited.Add(parent.Id))
                throw Reconciliation("The accepted bundle hierarchy contains a missing parent or cycle.");
            parentId = parent.ParentOrderItemId;
        }
    }

    private static long[] RootWeights(IReadOnlyList<OrderItem> roots, AccountMoney money)
    {
        var weights = roots.Select(item => ToMinor(money, item.ItemTotal, false)).ToArray();
        return weights.Length > 0 && weights.All(weight => weight == 0)
            ? roots.Select(item => (long)item.Quantity).ToArray()
            : weights;
    }

    private static int CountUnits(IReadOnlyList<OrderItem> roots, int maximumUnitRows)
    {
        long total = 0;
        foreach (var root in roots)
        {
            total = checked(total + root.Quantity);
            if (total > maximumUnitRows)
                throw Reconciliation("The accepted order exceeds the configured billing snapshot unit capacity.");
        }
        return checked((int)total);
    }

    private static void ValidatePricingAmounts(Order order)
    {
        if (order.Discount < 0 || order.CustomerDiscountAmount < 0 || order.DeliveryFee < 0
            || order.Tip < 0
            || order.FidelityPointsRedeemed < 0 || order.FidelityPointsDiscount < 0)
            throw Reconciliation("The accepted order contains a negative discount, fee or redemption.");
    }

    private static RedemptionFacts ValidateRedemption(
        Order order, AccountMoney money, OrderBillingRedemptionEvidence? evidence)
    {
        if (order.FidelityPointsRedeemed == 0)
        {
            if (evidence is not null || order.FidelityPointsDiscount != 0)
                throw Reconciliation("A points discount requires a matching persisted redemption row.");
            return new(0, 0, null, null, null, null, null, null, 0m);
        }

        if (evidence is null || evidence.TransactionId == Guid.Empty || !order.UserId.HasValue
            || evidence.UserId != order.UserId.Value || evidence.OrderId != order.Id
            || evidence.TransactionType != TransactionType.Redeemed || evidence.Points >= 0
            || evidence.OrderTotal is not null || evidence.CreatedAt == default)
            throw Reconciliation("The redemption row does not prove this order's exact negative debit.");

        int redeemedPoints;
        try
        {
            redeemedPoints = checked(-evidence.Points);
        }
        catch (OverflowException)
        {
            throw Reconciliation("The redemption points exceed the supported integer range.");
        }

        var discount = checked((decimal)redeemedPoints / RedemptionPointsPerMajorUnit);
        var discountMinor = ToMinor(money, discount, false);
        if (redeemedPoints != order.FidelityPointsRedeemed
            || discount != order.FidelityPointsDiscount || evidence.DiscountAmount != discount
            || discountMinor == 0)
            throw Reconciliation("The redemption debit, order points and applied discount do not agree.");
        return new(redeemedPoints, discountMinor, evidence.TransactionId, evidence.UserId,
            evidence.TransactionType, evidence.Points, evidence.OrderTotal, evidence.CreatedAt,
            evidence.DiscountAmount);
    }

    private static EarningFacts ValidateEarning(
        Order order,
        IReadOnlyList<OrderItem> roots,
        AccountMoney money,
        OrderBillingEarningEvaluation? evaluation)
    {
        var basisMinor = SumMinor(roots.Select(item => ToMinor(money, item.ItemTotal, false)));
        if (evaluation is null || evaluation.CandidatePoints is null)
        {
            if (evaluation is not null && (evaluation.AlgorithmVersion is not null
                || evaluation.RuleSetFingerprint is not null || evaluation.MatchedRule is not null))
                throw Reconciliation("Unevaluated loyalty evidence cannot carry a partial rule result.");
            if (order.FidelityPointsEarned != 0)
                throw Reconciliation("The order's loyalty preview has no matching evaluation evidence.");
            return new(basisMinor, null, null, null, null, null, null);
        }

        var candidatePoints = evaluation.CandidatePoints.Value;
        if (candidatePoints < 0 || string.IsNullOrWhiteSpace(evaluation.AlgorithmVersion)
            || !IsFingerprint(evaluation.RuleSetFingerprint) || !order.UserId.HasValue
            || candidatePoints != order.FidelityPointsEarned)
            throw Reconciliation("An evaluated loyalty candidate requires a version and rule-set fingerprint.");

        var rule = evaluation.MatchedRule;
        if (rule is null && candidatePoints != 0)
            throw Reconciliation("A positive loyalty candidate requires its matched rule evidence.");
        if (rule is not null && (rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name)
            || rule.MinimumOrderAmount < 0
            || (rule.MaximumOrderAmount.HasValue && rule.MaximumOrderAmount.Value < rule.MinimumOrderAmount)
            || rule.PointsAwarded < 0 || rule.PointsAwarded != candidatePoints))
            throw Reconciliation("The matched earning rule does not explain the frozen candidate.");

        long? minimumMinor = rule is null ? null : ToMinor(money, rule.MinimumOrderAmount, false);
        long? maximumMinor = rule is { MaximumOrderAmount: { } maximumOrderAmount }
            ? ToMinor(money, maximumOrderAmount, false) : null;
        if (rule is not null && (basisMinor < minimumMinor!.Value
            || (maximumMinor.HasValue && basisMinor > maximumMinor.Value)))
            throw Reconciliation("The matched earning rule does not apply to the frozen raw-root basis.");
        return new(basisMinor, candidatePoints, evaluation.AlgorithmVersion,
            evaluation.RuleSetFingerprint, rule, minimumMinor, maximumMinor);
    }

    private static bool IsFingerprint(string? value) => value is { Length: 64 }
        && Regex.IsMatch(value, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private static decimal ReadRawCourtesy(Order order)
    {
        try
        {
            var sale = checked(order.SubTotal + order.Tax + order.DeliveryFee
                - order.Discount - order.CustomerDiscountAmount);
            var roundedSale = PriceRoundingUtility.ApplySpecialRounding(sale,
                PriceRoundingUtility.HasActiveDiscount(order.CustomerDiscountAmount + order.Discount));
            return checked(roundedSale - sale);
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted pricing decomposition exceeds the supported decimal range.");
        }
    }

    private static void ValidateAcceptedTotal(
        Order order, AccountMoney money, long totalMinor, long tipMinor)
    {
        try
        {
            var sale = checked(order.SubTotal + order.Tax + order.DeliveryFee
                - order.Discount - order.CustomerDiscountAmount);
            var rounded = PriceRoundingUtility.ApplySpecialRounding(sale,
                PriceRoundingUtility.HasActiveDiscount(order.CustomerDiscountAmount + order.Discount));
            var acceptedTotal = checked(Math.Max(0m, rounded - order.FidelityPointsDiscount)
                + Math.Max(0m, order.Tip));
            if (ToMinor(money, acceptedTotal, false) != totalMinor
                || Math.Max(0m, order.Tip) != money.ToMajor(tipMinor))
                throw Reconciliation("The accepted total does not match the stored pricing components.");
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted total exceeds the supported decimal range.");
        }
    }

    private sealed record RedemptionFacts(
        int Points,
        long DiscountMinor,
        Guid? TransactionId,
        Guid? UserId,
        TransactionType? TransactionType,
        int? TransactionPoints,
        decimal? TransactionOrderTotal,
        DateTime? TransactionCreatedAt,
        decimal RawDiscountAmount);

    private sealed record EarningFacts(
        long BasisMinor,
        int? CandidatePoints,
        string? AlgorithmVersion,
        string? RuleSetFingerprint,
        OrderBillingEarningRuleEvidence? Rule,
        long? RuleMinimumMinor,
        long? RuleMaximumMinor);
}
