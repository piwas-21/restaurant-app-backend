using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderBillingSnapshotFactoryTests
{
    private static readonly Guid OrderId = Guid.Parse("93000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("93000000-0000-0000-0000-000000000002");
    private static readonly Guid FirstItemId = Guid.Parse("93000000-0000-0000-0000-000000000003");
    private static readonly Guid SecondItemId = Guid.Parse("93000000-0000-0000-0000-000000000004");

    [Fact]
    public void One_cent_remainders_follow_frozen_line_then_ordinal_policy()
    {
        var order = Source(1.01m, Item(FirstItemId, 3, 1.01m));

        var result = Build(order);

        result.Header.EarningBasisMinor.Should().Be(101);
        result.Header.PayableFoodMinor.Should().Be(101);
        result.Units.Select(unit => unit.PayableFoodMinor).Should().Equal(34, 34, 33);
        result.Units.Select(unit => unit.EarningBasisMinor).Should().Equal(34, 34, 33);
        result.Units.Sum(unit => unit.PayableFoodMinor).Should().Be(101);
    }

    [Fact]
    public void Shuffled_roots_preserve_two_stage_money_and_fixed_point_allocations()
    {
        var order = Source(3.01m, Item(SecondItemId, 1, 2m), Item(FirstItemId, 3, 1.01m));
        order.UserId = UserId;
        order.FidelityPointsEarned = 7;
        var earning = Evaluated(7, new(FirstItemId, "priority rule", 0m, null, 7, 1));

        var first = Build(order, earning: earning);
        order.Items = order.Items.Reverse().ToList();
        var shuffled = Build(order, earning: earning);

        shuffled.Header.Should().BeEquivalentTo(first.Header, options => options.Excluding(value => value.Id));
        shuffled.Units.OrderBy(unit => unit.OrderItemId).ThenBy(unit => unit.UnitOrdinal)
            .Select(UnitValues).Should().Equal(first.Units.OrderBy(unit => unit.OrderItemId)
                .ThenBy(unit => unit.UnitOrdinal).Select(UnitValues));
        first.Units.Sum(unit => unit.EarnedPoints).Should().Be(7);
    }

    [Fact]
    public void Over_discount_floor_is_a_named_signed_food_reconciliation()
    {
        var order = Source(0m, Item(FirstItemId, 3, 1m));
        order.Discount = 1m;
        order.CustomerDiscountAmount = 1.30m;

        var result = Build(order);

        result.Header.CourtesyRoundingMinor.Should().Be(30);
        result.Header.PayableFoodMinor.Should().Be(0);
        result.Header.FoodReconciliationMinor.Should().Be(100);
        result.Units.Select(unit => unit.FoodReconciliationMinor).Should().Equal(34, 33, 33);
        result.Units.Sum(unit => unit.FoodReconciliationMinor)
            .Should().Be(result.Header.FoodReconciliationMinor);
    }

    [Fact]
    public void Courtesy_rounding_can_be_negative_and_still_conserves_food()
    {
        var order = Source(10m, Item(FirstItemId, 1, 10.02m));
        order.Discount = 0.01m;

        var result = Build(order);

        result.Header.CourtesyRoundingMinor.Should().Be(-1);
        result.Header.FoodReconciliationMinor.Should().Be(0);
        result.Units.Single().CourtesyRoundingMinor.Should().Be(-1);
    }

    [Fact]
    public void Tip_and_charged_fee_stay_outside_payable_food()
    {
        var order = Source(15.01m,
            Item(FirstItemId, 1, 5m), Item(SecondItemId, 1, 5.01m));
        order.DeliveryFee = 3m;
        order.Tip = 2m;

        var result = Build(order);

        result.Header.PayableFoodMinor.Should().Be(1_001);
        result.Header.DeliveryFeeMinor.Should().Be(300);
        result.Header.ChargedDeliveryFeeMinor.Should().Be(300);
        result.Header.TipMinor.Should().Be(200);
        result.Header.TotalMinor.Should().Be(1_501);
        result.Header.FoodReconciliationMinor.Should().Be(0);
    }

    [Fact]
    public void Unpaid_nominal_fee_does_not_create_food_or_exceed_the_accepted_charge()
    {
        var order = Source(0m, Item(FirstItemId, 1, 10m));
        order.DeliveryFee = 4m;
        order.CustomerDiscountAmount = 15m;

        var result = Build(order);

        result.Header.DeliveryFeeMinor.Should().Be(400);
        result.Header.ChargedDeliveryFeeMinor.Should().Be(0);
        result.Header.PayableFoodMinor.Should().Be(0);
        result.Header.TotalMinor.Should().Be(0);
        result.Header.FoodReconciliationMinor.Should().Be(500);
    }

    [Fact]
    public void Fractional_cent_discount_is_frozen_with_raw_value_and_versioned_minor_quantization()
    {
        var order = Source(9m, Item(FirstItemId, 1, 10.01m));
        order.CustomerDiscountAmount = 1.5015m;

        var result = Build(order);

        result.Header.RawCustomerDiscountAmount.Should().Be(1.5015m);
        result.Header.RawCourtesyRoundingAmount.Should().Be(0.4915m);
        result.Header.ComponentQuantizationPolicyVersion.Should()
            .Be("currency-minor-2dp-away-from-zero-v1");
        result.Header.CustomerDiscountMinor.Should().Be(150);
        result.Header.CourtesyRoundingMinor.Should().Be(49);
        result.Header.TotalMinor.Should().Be(900);
        order.Total.Should().Be(9m, "the accepted raw pricing formula remains authoritative");
    }

    [Fact]
    public void Half_cent_components_quantize_away_from_zero_for_both_signs()
    {
        var positive = Source(11m, Item(FirstItemId, 1, 11m));
        positive.CustomerDiscountAmount = 0.005m;
        var negative = Source(10m, Item(FirstItemId, 1, 10.01m));
        negative.CustomerDiscountAmount = 0.005m;

        var positiveResult = Build(positive);
        var negativeResult = Build(negative);

        positiveResult.Header.CustomerDiscountMinor.Should().Be(1);
        positiveResult.Header.RawCourtesyRoundingAmount.Should().Be(0.005m);
        positiveResult.Header.CourtesyRoundingMinor.Should().Be(1);
        negativeResult.Header.CustomerDiscountMinor.Should().Be(1);
        negativeResult.Header.RawCourtesyRoundingAmount.Should().Be(-0.005m);
        negativeResult.Header.CourtesyRoundingMinor.Should().Be(-1);
        negativeResult.Header.FoodReconciliationMinor.Should().Be(1);
    }

    [Fact]
    public void Evaluated_zero_and_unevaluated_candidate_remain_distinct()
    {
        var order = Source(1m, Item(FirstItemId, 1, 1m));
        order.UserId = UserId;

        var unevaluated = Build(order);
        order.FidelityPointsEarned = 0;
        var evaluated = Build(order, earning: Evaluated(0));

        unevaluated.Header.EarnedPointsCandidate.Should().BeNull();
        evaluated.Header.EarnedPointsCandidate.Should().Be(0);
        evaluated.Header.EarningEvaluationVersion.Should().Be("fixed-priority-v1");
        unevaluated.OwnerLinks.Should().BeEmpty();
        var earningLink = evaluated.OwnerLinks.Should().ContainSingle().Which;
        earningLink.OrderId.Should().Be(OrderId);
        earningLink.Slot.Should().Be(OrderBillingSnapshotOwnerSlot.Earning);
        earningLink.UserId.Should().Be(UserId);
        earningLink.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Linked);
        earningLink.ErasedAt.Should().BeNull();
        earningLink.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
        evaluated.Header.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
    }

    [Fact]
    public void Positive_evaluated_candidate_requires_its_matched_rule()
    {
        var order = Source(1m, Item(FirstItemId, 1, 1m));
        order.UserId = UserId;
        order.FidelityPointsEarned = 1;

        var act = () => Build(order, earning: Evaluated(1));

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Actual_redemption_requires_exact_negative_order_and_user_evidence()
    {
        var order = Source(9.75m, Item(FirstItemId, 1, 10m));
        order.UserId = UserId;
        order.FidelityPointsRedeemed = 25;
        order.FidelityPointsDiscount = 0.25m;
        var transactionCreatedAt = DateTime.UnixEpoch.AddMinutes(4);
        var evidence = new OrderBillingRedemptionEvidence(Guid.NewGuid(), UserId, OrderId,
            TransactionType.Redeemed, -25, 0.25m, null, transactionCreatedAt);

        var result = Build(order, redemption: evidence);

        result.Header.RedemptionTransactionId.Should().Be(evidence.TransactionId);
        result.Header.RedemptionTransactionType.Should().Be(evidence.TransactionType);
        result.Header.RedemptionTransactionPoints.Should().Be(evidence.Points);
        result.Header.RedemptionTransactionOrderTotal.Should().Be(evidence.OrderTotal);
        result.Header.RedemptionTransactionCreatedAt.Should().Be(transactionCreatedAt);
        var redemptionLink = result.OwnerLinks.Should().ContainSingle().Which;
        redemptionLink.OrderId.Should().Be(OrderId);
        redemptionLink.Slot.Should().Be(OrderBillingSnapshotOwnerSlot.Redemption);
        redemptionLink.UserId.Should().Be(UserId);
        redemptionLink.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Linked);
        redemptionLink.ErasedAt.Should().BeNull();
        redemptionLink.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
        result.Header.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
        result.Header.RedemptionDiscountMinor.Should().Be(25);
        result.Header.FoodReconciliationMinor.Should().Be(0);
    }

    [Theory]
    [InlineData(false, true, TransactionType.Redeemed, -25, 25)]
    [InlineData(true, false, TransactionType.Redeemed, -25, 25)]
    [InlineData(false, false, TransactionType.Earned, -25, 25)]
    [InlineData(false, false, TransactionType.Redeemed, -24, 25)]
    [InlineData(false, false, TransactionType.Redeemed, -25, 24)]
    public void Mismatched_redemption_is_rejected(
        bool wrongOwner, bool wrongOrder, TransactionType type, int points, int discountMinor)
    {
        var order = Source(9.75m, Item(FirstItemId, 1, 10m));
        order.UserId = UserId;
        order.FidelityPointsRedeemed = 25;
        order.FidelityPointsDiscount = 0.25m;
        var evidence = new OrderBillingRedemptionEvidence(Guid.NewGuid(),
            wrongOwner ? Guid.NewGuid() : UserId,
            wrongOrder ? Guid.NewGuid() : OrderId, type, points, discountMinor / 100m, null, DateTime.UnixEpoch);

        var act = () => Build(order, redemption: evidence);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Tax_policy_is_zero_only_and_does_not_reconcile_nonzero_tax()
    {
        var order = Source(1.08m, Item(FirstItemId, 1, 1m));
        order.Tax = 0.08m;

        var act = () => Build(order);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Corrupted_total_or_component_decomposition_is_rejected_before_reconciliation()
    {
        var wrongTotal = Source(1.02m, Item(FirstItemId, 1, 1m));
        var wrongComponents = Source(1.01m, Item(FirstItemId, 1, 1m));
        wrongComponents.SubTotal = 1.01m;
        var changedRoot = Source(1.01m, Item(FirstItemId, 1, 1.01m));
        changedRoot.Items.Single().ItemTotal = 1.02m;

        var totalAct = () => Build(wrongTotal);
        var decompositionAct = () => Build(wrongComponents);
        var changedRootAct = () => Build(changedRoot);

        totalAct.Should().Throw<ConflictException>();
        decompositionAct.Should().Throw<ConflictException>();
        changedRootAct.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Child_rows_cannot_own_charge_or_escape_the_order_item_graph()
    {
        var root = Item(FirstItemId, 1, 1m);
        var child = Item(SecondItemId, 1, 0m);
        child.ParentOrderItemId = FirstItemId;
        var order = Source(1m, root, child);

        Build(order).Units.Should().ContainSingle();
        child.ItemTotal = 0.01m;
        var chargedChild = () => Build(order);
        chargedChild.Should().Throw<ConflictException>();

        child.ItemTotal = 0m;
        child.ParentOrderItemId = Guid.NewGuid();
        var detachedChild = () => Build(order);
        detachedChild.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Unit_capacity_is_checked_before_row_expansion()
    {
        var atLimit = Source(10m, Item(FirstItemId, OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows, 10m));
        Build(atLimit, maximumUnitRows: OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows)
            .Units.Should().HaveCount(OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows);

        var overByAggregate = Source(2m,
            Item(FirstItemId, 6_000, 1m), Item(SecondItemId, 5_000, 1m));
        var extreme = Source(0.01m, Item(FirstItemId, int.MaxValue, 0.01m));

        var aggregateAct = () => Build(overByAggregate, maximumUnitRows: 10_000);
        var extremeAct = () => Build(extreme, maximumUnitRows: 10_000);

        aggregateAct.Should().Throw<ConflictException>();
        extremeAct.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Invalid_unit_capacity_is_rejected(int maximumUnitRows)
    {
        var order = Source(1m, Item(FirstItemId, 1, 1m));

        var act = () => Build(order, maximumUnitRows: maximumUnitRows);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Decimal_or_minor_unit_overflow_fails_closed()
    {
        var order = Source(decimal.MaxValue, Item(FirstItemId, 1, decimal.MaxValue));

        var act = () => Build(order);

        act.Should().Throw<ConflictException>();
    }

    private static OrderBillingSnapshotBuildResult Build(
        Order order,
        OrderBillingEarningEvaluation? earning = null,
        OrderBillingRedemptionEvidence? redemption = null,
        int maximumUnitRows = 1_000) =>
        OrderBillingSnapshotFactory.Build(order, "CHF", earning, redemption, maximumUnitRows);

    private static OrderBillingEarningEvaluation Evaluated(
        int candidate, OrderBillingEarningRuleEvidence? rule = null) =>
        new(candidate, "fixed-priority-v1", new string('a', 64), rule);

    private static Order Source(decimal total, params OrderItem[] items) => new()
    {
        Id = OrderId,
        SubTotal = items.Where(item => item.ParentOrderItemId is null).Sum(item => item.ItemTotal),
        Total = total,
        Items = items.ToList(),
        CreatedAt = DateTime.UnixEpoch,
        CreatedBy = "snapshot-test"
    };

    private static OrderItem Item(Guid id, int quantity, decimal itemTotal) => new()
    {
        Id = id,
        OrderId = OrderId,
        Quantity = quantity,
        ItemTotal = itemTotal,
        CreatedAt = DateTime.UnixEpoch,
        CreatedBy = "snapshot-test"
    };

    private static object UnitValues(OrderBillingSnapshotUnit unit) => new
    {
        unit.OrderItemId,
        unit.UnitOrdinal,
        unit.GrossFoodMinor,
        unit.OrderDiscountMinor,
        unit.CustomerDiscountMinor,
        unit.CourtesyRoundingMinor,
        unit.PayableFoodMinor,
        unit.FoodReconciliationMinor,
        unit.EarningBasisMinor,
        unit.EarnedPoints
    };
}
