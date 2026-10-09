using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static partial class OrderBillingSnapshotFactory
{
    private static List<OrderBillingSnapshotUnit> BuildUnits(UnitAllocationContext context)
    {
        var roots = context.RootAllocation.Roots;
        var weights = context.RootAllocation.Weights;
        var header = context.Header;
        var redemption = context.Redemption;
        var earning = context.Earning;
        var gross = AllocateRootThenOrdinal(header.GrossFoodMinor, roots, weights);
        var orderDiscount = AllocateRootThenOrdinal(header.OrderDiscountMinor, roots, weights);
        var customerDiscount = AllocateRootThenOrdinal(header.CustomerDiscountMinor, roots, weights);
        var rounding = AllocateRootThenOrdinal(header.CourtesyRoundingMinor, roots, weights);
        var redemptionDiscount = AllocateRootThenOrdinal(header.RedemptionDiscountMinor, roots, weights);
        var redeemedPoints = AllocateRootThenOrdinal(redemption.Points, roots, weights);
        var earnedPoints = AllocateRootThenOrdinal(earning.CandidatePoints ?? 0, roots, weights);
        var payableFood = AllocateFrozenFood(context.Charge);

        var units = new List<OrderBillingSnapshotUnit>(context.RootAllocation.UnitCount);
        foreach (var root in roots)
        {
            var lineGross = ToMinor(context.Money, root.ItemTotal, false);
            for (var ordinal = 1; ordinal <= root.Quantity; ordinal++)
            {
                var key = (root.Id, ordinal);
                // This signed row residual is derived after every accepted component uses the
                // frozen root-then-ordinal allocation. Re-apportioning it independently can
                // break the per-unit equation when separate largest-remainder columns tie.
                var unitReconciliation = CalculateFoodReconciliation(payableFood[key], gross[key], 0,
                    orderDiscount[key], customerDiscount[key], rounding[key], redemptionDiscount[key]);
                units.Add(new OrderBillingSnapshotUnit
                {
                    Id = Guid.NewGuid(),
                    OrderId = header.OrderId,
                    OrderItemId = root.Id,
                    UnitOrdinal = ordinal,
                    GrossFoodMinor = AccountShareMath.Equal(lineGross, root.Quantity).At(ordinal),
                    TaxMinor = 0,
                    TaxRateBasisPoints = 0,
                    TaxCategory = "none",
                    TaxTreatment = OrderBillingTaxTreatment.NotApplied,
                    OrderDiscountMinor = orderDiscount[key],
                    CustomerDiscountMinor = customerDiscount[key],
                    CourtesyRoundingMinor = rounding[key],
                    RedeemedPoints = checked((int)redeemedPoints[key]),
                    RedemptionDiscountMinor = redemptionDiscount[key],
                    PayableFoodMinor = payableFood[key],
                    FoodReconciliationMinor = unitReconciliation,
                    EarningBasisMinor = AccountShareMath.Equal(lineGross, root.Quantity).At(ordinal),
                    EarnedPoints = checked((int)earnedPoints[key]),
                    CreatedAt = header.CreatedAt,
                    CreatedBy = header.CreatedBy
                });
            }
        }

        if (SumMinor(units.Select(unit => unit.FoodReconciliationMinor)) != header.FoodReconciliationMinor)
            throw Reconciliation("The unit food adjustments do not conserve the accepted header adjustment.");
        return units;
    }

    private static Dictionary<(Guid ItemId, int Ordinal), long> AllocateFrozenFood(FrozenOrderCharge charge)
    {
        var shares = new Dictionary<(Guid ItemId, int Ordinal), long>();
        foreach (var line in charge.FoodLines)
        {
            var distribution = AccountShareMath.Equal(line.AmountMinor, line.Item.Quantity);
            for (var ordinal = 1; ordinal <= line.Item.Quantity; ordinal++)
                shares[(line.Item.Id, ordinal)] = distribution.At(ordinal);
        }
        return shares;
    }

    private static Dictionary<(Guid ItemId, int Ordinal), long> AllocateRootThenOrdinal(
        long signedAmount, IReadOnlyList<OrderItem> roots, IReadOnlyList<long> weights)
    {
        var result = new Dictionary<(Guid ItemId, int Ordinal), long>();
        if (roots.Count == 0)
        {
            if (signedAmount != 0)
                throw Reconciliation("A nonzero food component cannot be allocated without root units.");
            return result;
        }

        var negative = signedAmount < 0;
        long magnitude;
        try
        {
            magnitude = negative ? checked(-signedAmount) : signedAmount;
        }
        catch (OverflowException)
        {
            throw Reconciliation("A signed billing allocation exceeds the supported integer range.");
        }
        var rootAmounts = AccountShareMath.Weighted(magnitude, weights);
        for (var index = 0; index < roots.Count; index++)
        {
            var distribution = AccountShareMath.Equal(rootAmounts[index], roots[index].Quantity);
            for (var ordinal = 1; ordinal <= roots[index].Quantity; ordinal++)
            {
                var value = distribution.At(ordinal);
                result[(roots[index].Id, ordinal)] = negative ? checked(-value) : value;
            }
        }
        return result;
    }

    private static void ValidateUnitConservation(
        List<OrderBillingSnapshotUnit> units,
        OrderBillingSnapshot header)
    {
        if (SumMinor(units.Select(unit => unit.GrossFoodMinor)) != header.GrossFoodMinor
            || SumMinor(units.Select(unit => unit.TaxMinor)) != header.TaxMinor
            || SumMinor(units.Select(unit => unit.OrderDiscountMinor)) != header.OrderDiscountMinor
            || SumMinor(units.Select(unit => unit.CustomerDiscountMinor)) != header.CustomerDiscountMinor
            || SumMinor(units.Select(unit => unit.CourtesyRoundingMinor)) != header.CourtesyRoundingMinor
            || SumMinor(units.Select(unit => unit.RedemptionDiscountMinor)) != header.RedemptionDiscountMinor
            || SumMinor(units.Select(unit => (long)unit.RedeemedPoints)) != header.RedeemedPoints
            || SumMinor(units.Select(unit => unit.PayableFoodMinor)) != header.PayableFoodMinor
            || SumMinor(units.Select(unit => unit.FoodReconciliationMinor)) != header.FoodReconciliationMinor
            || SumMinor(units.Select(unit => unit.EarningBasisMinor)) != header.EarningBasisMinor
            || SumMinor(units.Select(unit => (long)unit.EarnedPoints)) != (header.EarnedPointsCandidate ?? 0)
            || units.Any(unit => unit.FoodReconciliationMinor != CalculateFoodReconciliation(
                unit.PayableFoodMinor, unit.GrossFoodMinor, unit.TaxMinor,
                unit.OrderDiscountMinor, unit.CustomerDiscountMinor,
                unit.CourtesyRoundingMinor, unit.RedemptionDiscountMinor)))
            throw Reconciliation("The per-unit billing and loyalty allocations do not conserve their headers.");
    }
}
