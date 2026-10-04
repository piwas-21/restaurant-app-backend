using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static partial class OrderBillingSnapshotFactory
{
    internal const string PricingPolicyVersion = "native-zero-tax-v1";
    internal const string ComponentQuantizationPolicyVersion = "currency-minor-2dp-away-from-zero-v1";
    internal const string EarningBasisPolicyVersion = "raw-root-item-total-v1";
    internal const int RedemptionPointsPerMajorUnit = 100;

    internal static OrderBillingSnapshotBuildResult Build(
        Order order,
        string? acceptedCurrency,
        OrderBillingEarningEvaluation? earningEvaluation,
        OrderBillingRedemptionEvidence? redemption,
        int maximumUnitRows)
    {
        ArgumentNullException.ThrowIfNull(order);
        ValidateCapacity(maximumUnitRows);
        ValidateOrderIdentity(order);

        var money = CreateMoney(acceptedCurrency);
        var items = order.Items?.ToArray()
            ?? throw Reconciliation("A billing snapshot requires the accepted order item collection.");
        var roots = ValidateItems(order, items, money);
        var rootWeights = RootWeights(roots, money);
        var unitCount = CountUnits(roots, maximumUnitRows);
        ValidatePricingAmounts(order);
        var redemptionFacts = ValidateRedemption(order, money, redemption);
        var earningFacts = ValidateEarning(order, roots, money, earningEvaluation);
        var charge = FrozenOrderChargeMath.Read(order, money);

        var grossMinor = SumMinor(roots.Select(item => ToMinor(money, item.ItemTotal, false)));
        var subTotalMinor = ToMinor(money, order.SubTotal, false);
        var taxMinor = ToMinor(money, order.Tax, false);
        if (taxMinor != 0 || grossMinor != subTotalMinor)
            throw Reconciliation("The accepted root-item total or current zero-tax policy does not match the order.");

        var orderDiscountMinor = QuantizeToMinor(money, order.Discount, false);
        var customerDiscountMinor = QuantizeToMinor(money, order.CustomerDiscountAmount, false);
        var feeMinor = ToMinor(money, order.DeliveryFee, false);
        var tipMinor = ToMinor(money, order.Tip, false);
        var totalMinor = ToMinor(money, order.Total, false);
        var pointsDiscountMinor = redemptionFacts.DiscountMinor;
        var rawCourtesy = ReadRawCourtesy(order);
        var roundingMinor = QuantizeToMinor(money, rawCourtesy, true);

        ValidateAcceptedTotal(order, money, totalMinor, tipMinor);
        if (charge.TotalMinor != totalMinor || charge.TipMinor != tipMinor)
            throw Reconciliation("The accepted charge could not be separated into protected food, fee and tip.");

        var reconciliationMinor = CalculateFoodReconciliation(charge.FoodMinor, grossMinor, taxMinor,
            orderDiscountMinor, customerDiscountMinor, roundingMinor, pointsDiscountMinor);
        ValidateFoodTotal(charge.FoodMinor, charge.FeeMinor, tipMinor, totalMinor);

        var header = BuildHeader(new HeaderBuildContext(order, money,
            new SnapshotHeaderAmounts
            {
                GrossFoodMinor = grossMinor,
                TaxMinor = taxMinor,
                DeliveryFeeMinor = feeMinor,
                ChargedDeliveryFeeMinor = charge.FeeMinor,
                OrderDiscountMinor = orderDiscountMinor,
                CustomerDiscountMinor = customerDiscountMinor,
                CourtesyRoundingMinor = roundingMinor,
                PayableFoodMinor = charge.FoodMinor,
                TipMinor = tipMinor,
                TotalMinor = totalMinor,
                FoodReconciliationMinor = reconciliationMinor
            },
            new SnapshotRawPricing
            {
                TaxAmount = order.Tax,
                OrderDiscountAmount = order.Discount,
                CustomerDiscountAmount = order.CustomerDiscountAmount,
                RedemptionDiscountAmount = redemptionFacts.RawDiscountAmount,
                CourtesyRoundingAmount = rawCourtesy
            },
            redemptionFacts, earningFacts));
        var allocation = new RootUnitAllocation(roots, rootWeights, unitCount);
        var units = BuildUnits(new UnitAllocationContext(
            allocation, money, charge, header, redemptionFacts, earningFacts));
        ValidateUnitConservation(units, header);
        return new(header, units);
    }

    private static OrderBillingSnapshot BuildHeader(HeaderBuildContext context)
    {
        var amounts = context.Amounts;
        var raw = context.RawPricing;
        var redemption = context.Redemption;
        var earning = context.Earning;
        var rule = earning.Rule;
        return new OrderBillingSnapshot
        {
            Id = Guid.NewGuid(),
            OrderId = context.Order.Id,
            Currency = context.Money.Currency,
            PricingPolicyVersion = PricingPolicyVersion,
            ComponentQuantizationPolicyVersion = ComponentQuantizationPolicyVersion,
            EarningBasisPolicyVersion = EarningBasisPolicyVersion,
            GrossFoodMinor = amounts.GrossFoodMinor,
            TaxMinor = amounts.TaxMinor,
            DeliveryFeeMinor = amounts.DeliveryFeeMinor,
            ChargedDeliveryFeeMinor = amounts.ChargedDeliveryFeeMinor,
            OrderDiscountMinor = amounts.OrderDiscountMinor,
            CustomerDiscountMinor = amounts.CustomerDiscountMinor,
            CourtesyRoundingMinor = amounts.CourtesyRoundingMinor,
            RedeemedPoints = redemption.Points,
            RedemptionDiscountMinor = redemption.DiscountMinor,
            PayableFoodMinor = amounts.PayableFoodMinor,
            TipMinor = amounts.TipMinor,
            TotalMinor = amounts.TotalMinor,
            FoodReconciliationMinor = amounts.FoodReconciliationMinor,
            RawTaxAmount = raw.TaxAmount,
            RawOrderDiscountAmount = raw.OrderDiscountAmount,
            RawCustomerDiscountAmount = raw.CustomerDiscountAmount,
            RawRedemptionDiscountAmount = raw.RedemptionDiscountAmount,
            RawCourtesyRoundingAmount = raw.CourtesyRoundingAmount,
            EarningBasisMinor = earning.BasisMinor,
            EarnedPointsCandidate = earning.CandidatePoints,
            EarningUserId = earning.CandidatePoints.HasValue ? context.Order.UserId : null,
            EarningEvaluationVersion = earning.AlgorithmVersion,
            EarningRuleSetFingerprint = earning.RuleSetFingerprint,
            EarningRuleId = rule?.Id,
            EarningRuleName = rule?.Name,
            EarningRuleMinimumMinor = earning.RuleMinimumMinor,
            EarningRuleMaximumMinor = earning.RuleMaximumMinor,
            EarningRulePoints = rule?.PointsAwarded,
            EarningRulePriority = rule?.Priority,
            RedemptionTransactionId = redemption.TransactionId,
            RedemptionUserId = redemption.UserId,
            TaxCategory = "none",
            TaxRateBasisPoints = 0,
            TaxTreatment = OrderBillingTaxTreatment.NotApplied,
            CreatedAt = context.Order.CreatedAt,
            CreatedBy = context.Order.CreatedBy
        };
    }

    private sealed record HeaderBuildContext(
        Order Order,
        AccountMoney Money,
        SnapshotHeaderAmounts Amounts,
        SnapshotRawPricing RawPricing,
        RedemptionFacts Redemption,
        EarningFacts Earning);

    private sealed record SnapshotHeaderAmounts
    {
        public required long GrossFoodMinor { get; init; }
        public required long TaxMinor { get; init; }
        public required long DeliveryFeeMinor { get; init; }
        public required long ChargedDeliveryFeeMinor { get; init; }
        public required long OrderDiscountMinor { get; init; }
        public required long CustomerDiscountMinor { get; init; }
        public required long CourtesyRoundingMinor { get; init; }
        public required long PayableFoodMinor { get; init; }
        public required long TipMinor { get; init; }
        public required long TotalMinor { get; init; }
        public required long FoodReconciliationMinor { get; init; }
    }

    private sealed record SnapshotRawPricing
    {
        public required decimal TaxAmount { get; init; }
        public required decimal OrderDiscountAmount { get; init; }
        public required decimal CustomerDiscountAmount { get; init; }
        public required decimal RedemptionDiscountAmount { get; init; }
        public required decimal CourtesyRoundingAmount { get; init; }
    }

    private sealed record RootUnitAllocation(
        IReadOnlyList<OrderItem> Roots,
        IReadOnlyList<long> Weights,
        int UnitCount);

    private sealed record UnitAllocationContext(
        RootUnitAllocation RootAllocation,
        AccountMoney Money,
        FrozenOrderCharge Charge,
        OrderBillingSnapshot Header,
        RedemptionFacts Redemption,
        EarningFacts Earning);
}

internal sealed record OrderBillingSnapshotBuildResult(
    OrderBillingSnapshot Header,
    IReadOnlyList<OrderBillingSnapshotUnit> Units);
