using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    private static AcceptedLoyaltySnapshot ValidateSnapshot(
        Order source, AccountMoney money, OrderAmendmentLoyaltyEvidence evidence)
    {
        var snapshot = evidence.Snapshot!;
        if (snapshot.Id == Guid.Empty || snapshot.OrderId != source.Id
            || snapshot.Currency != money.Currency || snapshot.TaxMinor != 0
            || source.Tax != 0 || snapshot.TaxCategory != "none" || snapshot.TaxRateBasisPoints != 0
            || snapshot.TaxTreatment != OrderBillingTaxTreatment.NotApplied
            || source.FidelityPointsEarned < 0 || source.FidelityPointsRedeemed < 0
            || source.FidelityPointsDiscount < 0 || snapshot.EarnedPointsCandidate < 0
            || snapshot.RedeemedPoints < 0)
            throw Held("The immutable loyalty snapshot does not match the accepted order currency or tax policy.");
        var units = evidence.Units;
        var items = source.Items.ToDictionary(value => value.Id);
        if (items.Count != source.Items.Count || units.Count == 0
            || units.Any(value => value.OrderId != source.Id || value.Id == Guid.Empty
                || value.OrderItemId == Guid.Empty || value.UnitOrdinal < 1
                || value.EarnedPoints < 0 || value.RedeemedPoints < 0 || value.TaxMinor != 0
                || !items.TryGetValue(value.OrderItemId, out var item)
                || item.ParentOrderItemId.HasValue || value.UnitOrdinal > item.Quantity)
            || units.Select(value => (value.OrderItemId, value.UnitOrdinal)).Distinct().Count() != units.Count)
            throw Held("The accepted loyalty snapshot has invalid unit ownership, bounds, or tax facts.");

        foreach (var root in items.Values.Where(value => !value.ParentOrderItemId.HasValue))
        {
            var rootUnits = units.Where(value => value.OrderItemId == root.Id)
                .OrderBy(value => value.UnitOrdinal).ToArray();
            if (root.Quantity <= 0 || rootUnits.Length != root.Quantity
                || rootUnits.Where((value, index) => value.UnitOrdinal != index + 1).Any())
                throw Held("The accepted loyalty snapshot does not contain the complete root-unit graph.");
        }

        var candidate = snapshot.EarnedPointsCandidate ?? 0;
        if (candidate != source.FidelityPointsEarned
            || units.Sum(value => (long)value.EarnedPoints) != candidate
            || units.Sum(value => (long)value.RedeemedPoints) != snapshot.RedeemedPoints
            || snapshot.RedeemedPoints != source.FidelityPointsRedeemed
            || money.ToMinor(source.FidelityPointsDiscount) != snapshot.RedemptionDiscountMinor
            || units.Sum(value => value.TaxMinor) != snapshot.TaxMinor)
            throw Held("The source-order point counters do not conserve the immutable unit snapshot.");
        if (!snapshot.EarnedPointsCandidate.HasValue && HasPartialEarningFacts(snapshot))
            throw Held("An unevaluated earning snapshot contains partial evaluation facts.");
        ValidateEarningRule(snapshot);
        ValidateEarningBasis(source, money, snapshot, units);
        ValidateRedemptionHeader(snapshot, money);
        var ownerLinks = ValidateOwnerLinks(source, snapshot, evidence.OwnerLinks);
        return new(snapshot, units, candidate, ownerLinks.Earning, ownerLinks.Redemption);
    }

    private static void ValidateEarningRule(OrderBillingSnapshot snapshot)
    {
        if (!snapshot.EarnedPointsCandidate.HasValue)
        {
            if (HasPartialEarningFacts(snapshot))
                throw Held("An unevaluated earning snapshot contains partial evaluation facts.");
            return;
        }
        var candidate = snapshot.EarnedPointsCandidate.Value;
        if (string.IsNullOrWhiteSpace(snapshot.EarningEvaluationVersion)
            || !IsFingerprint(snapshot.EarningRuleSetFingerprint))
            throw Held("The evaluated earning candidate has no versioned rule-set evidence.");
        if (candidate > 0 && (!snapshot.EarningRuleId.HasValue
                || snapshot.EarningRulePoints != candidate))
            throw Held("The positive earning candidate has no exact frozen rule evidence.");
        if (snapshot.EarningRuleId.HasValue && (string.IsNullOrWhiteSpace(snapshot.EarningRuleName)
                || !snapshot.EarningRuleMinimumMinor.HasValue || snapshot.EarningRuleMinimumMinor < 0
                || snapshot.EarningRulePoints != candidate
                || snapshot.EarningRuleMaximumMinor is long maximum
                    && maximum < snapshot.EarningRuleMinimumMinor))
            throw Held("The frozen earning rule evidence is inconsistent with its candidate.");
        if (!snapshot.EarningRuleId.HasValue && (snapshot.EarningRuleName is not null
                || snapshot.EarningRuleMinimumMinor.HasValue || snapshot.EarningRuleMaximumMinor.HasValue
                || snapshot.EarningRulePoints.HasValue || snapshot.EarningRulePriority.HasValue))
            throw Held("A no-match earning evaluation contains partial rule facts.");
    }

    private static void ValidateEarningBasis(
        Order source, AccountMoney money, OrderBillingSnapshot snapshot,
        IReadOnlyList<OrderBillingSnapshotUnit> units)
    {
        try
        {
            var rootBasis = source.Items.Where(value => !value.ParentOrderItemId.HasValue)
                .Sum(value => money.ToMinor(value.ItemTotal));
            if (rootBasis != snapshot.EarningBasisMinor
                || units.Sum(value => value.EarningBasisMinor) != snapshot.EarningBasisMinor
                || snapshot.EarningBasisMinor < 0)
                throw Held("The frozen earning basis does not match the accepted root-item subtotal.");
        }
        catch (BadRequestException exception)
        {
            throw Held("The accepted earning currency cannot represent its frozen root-item basis.", exception);
        }
    }

    private static void ValidateRedemptionHeader(OrderBillingSnapshot snapshot, AccountMoney money)
    {
        var transactionId = snapshot.RedemptionTransactionId;
        if (snapshot.RedeemedPoints == 0)
        {
            if (transactionId.HasValue || snapshot.RedemptionTransactionType.HasValue
                || snapshot.RedemptionTransactionPoints.HasValue
                || snapshot.RedemptionTransactionOrderTotal.HasValue
                || snapshot.RedemptionTransactionCreatedAt.HasValue
                || snapshot.RedemptionDiscountMinor != 0)
                throw Held("A zero redemption contains partial original-transaction evidence.");
            return;
        }
        int points;
        try { points = checked(-snapshot.RedemptionTransactionPoints!.Value); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        { throw Held("The frozen redemption transaction points are invalid.", exception); }
        long expectedDiscountMinor;
        try { expectedDiscountMinor = money.ToMinor(checked((decimal)points / 100m)); }
        catch (Exception exception) when (exception is OverflowException or BadRequestException)
        { throw Held("The frozen redemption discount cannot be represented by its accepted currency.", exception); }
        if (!transactionId.HasValue || transactionId == Guid.Empty
            || snapshot.RedemptionTransactionType != TransactionType.Redeemed
            || points != snapshot.RedeemedPoints
            || snapshot.RedemptionTransactionOrderTotal.HasValue
            || !snapshot.RedemptionTransactionCreatedAt.HasValue
            || snapshot.RedemptionTransactionCreatedAt.Value == default
            || snapshot.RedemptionDiscountMinor <= 0
            || snapshot.RedemptionDiscountMinor != expectedDiscountMinor)
            throw Held("The frozen redemption transaction does not match the accepted order debit.");
    }

    private static (OrderBillingSnapshotOwnerLink? Earning, OrderBillingSnapshotOwnerLink? Redemption)
        ValidateOwnerLinks(Order source, OrderBillingSnapshot snapshot,
            IReadOnlyList<OrderBillingSnapshotOwnerLink> links)
    {
        if (links.Select(value => value.Slot).Distinct().Count() != links.Count
            || links.Any(value => value.Slot is not (OrderBillingSnapshotOwnerSlot.Earning
                or OrderBillingSnapshotOwnerSlot.Redemption)))
            throw Held("The accepted order has duplicate loyalty owner links.");
        var earning = links.SingleOrDefault(value => value.Slot == OrderBillingSnapshotOwnerSlot.Earning);
        var redemption = links.SingleOrDefault(value => value.Slot == OrderBillingSnapshotOwnerSlot.Redemption);
        if (snapshot.EarnedPointsCandidate.HasValue
            && (earning is null || !IsCurrentOwner(earning, source.UserId)))
            throw Held("The accepted earning owner is unavailable or erased.");
        if (!snapshot.EarnedPointsCandidate.HasValue && earning is not null)
            throw Held("An unevaluated earning snapshot has an unexpected owner link.");
        if (snapshot.RedemptionTransactionId.HasValue
            && (redemption is null || !IsCurrentOwner(redemption, source.UserId)))
            throw Held("The accepted redemption owner is unavailable or erased.");
        if (!snapshot.RedemptionTransactionId.HasValue && redemption is not null)
            throw Held("A no-redemption snapshot has an unexpected redemption owner link.");
        return (earning, redemption);
    }

    private static bool IsCurrentOwner(OrderBillingSnapshotOwnerLink link, Guid? orderOwnerId) =>
        orderOwnerId.HasValue && link.OrderId != Guid.Empty && link.UserId == orderOwnerId
        && link.Disposition == OrderBillingSnapshotOwnerDisposition.Linked
        && !link.ErasedAt.HasValue && link.ErasureTransactionId is null;

    private static bool HasPartialEarningFacts(OrderBillingSnapshot value) =>
        value.EarningEvaluationVersion is not null || value.EarningRuleSetFingerprint is not null
        || value.EarningRuleId.HasValue || value.EarningRuleName is not null
        || value.EarningRuleMinimumMinor.HasValue || value.EarningRuleMaximumMinor.HasValue
        || value.EarningRulePoints.HasValue || value.EarningRulePriority.HasValue;

    private static bool IsFingerprint(string? value) => value is { Length: 64 }
        && Regex.IsMatch(value, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private sealed record AcceptedLoyaltySnapshot(
        OrderBillingSnapshot Snapshot,
        IReadOnlyList<OrderBillingSnapshotUnit> Units,
        int CandidatePoints,
        OrderBillingSnapshotOwnerLink? EarningOwnerLink,
        OrderBillingSnapshotOwnerLink? RedemptionOwnerLink);
}
