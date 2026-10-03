using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountDebtProjectionTests
{
    private static readonly Guid OrderId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid FirstItem = Guid.Parse("90000000-0000-0000-0000-000000000002");
    private static readonly Guid SecondItem = Guid.Parse("90000000-0000-0000-0000-000000000003");

    [Fact]
    public void FrozenTotalIncludesFeesAndDiscountsWithoutChargingBundleChildrenAgain()
    {
        var order = Round(320.01m, 0);
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            ParentOrderItemId = FirstItem,
            Quantity = 10,
            ItemTotal = 999m,
            CreatedBy = "test"
        });
        var result = AccountDebtProjection.Project([order], new("CHF"), [], []);
        result.OutstandingMinor.Should().Be(32001);
        result.Available.Should().Equal(
            new AccountDebtSegment(OrderId, FirstItem, 1, 1, 10667),
            new AccountDebtSegment(OrderId, SecondItem, 1, 2, 10667));
    }

    [Fact]
    public void HistoricalContributionsAndExplicitCapturedUnitsAreCountedOnce()
    {
        var order = Round(320.01m, 76.67m);
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 76.67m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var result = AccountDebtProjection.Project([order], new("EUR"),
            [new(OrderId, SecondItem, 2, 1, 6667)], [new(OrderId, SecondItem, 1, 1, 1000)]);
        result.OutstandingMinor.Should().Be(24334);
        result.ReservedMinor.Should().Be(1000);
        result.AvailableMinor.Should().Be(23334);
        result.Available.Should().Equal(
            new AccountDebtSegment(OrderId, FirstItem, 1, 1, 9667),
            new AccountDebtSegment(OrderId, SecondItem, 1, 1, 9667),
            new AccountDebtSegment(OrderId, SecondItem, 2, 1, 4000));
    }

    [Fact]
    public void ActualCapturedTendersTakePrecedenceOverAnOutdatedCachedSummary()
    {
        var order = Round(30m, 0);
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 10m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var result = AccountDebtProjection.Project([order], new("USD"), [], []);
        result.OutstandingMinor.Should().Be(2000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Positive_paid_summary_without_captured_tender_rows_requires_reconciliation(
        bool hasPendingPlaceholder)
    {
        var order = Round(30m, 5m);
        if (hasPendingPlaceholder)
        {
            order.Payments.Add(new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = OrderId,
                Amount = 5m,
                Status = PaymentStatus.Pending,
                CreatedBy = "test"
            });
        }

        var project = () => AccountDebtProjection.Project([order], new("CHF"), [], []);

        project.Should().Throw<ConflictException>().WithMessage("*without captured tender evidence*");
    }

    [Fact]
    public void CapturedAllocationsCannotExceedActualTenderEvidence()
    {
        var act = () => AccountDebtProjection.Project([Round(30m, 0)], new("CHF"),
            [new(OrderId, FirstItem, 1, 1, 1000)], []);
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void ReversedRoundsWithLiveAllocationsRequireReconciliation()
    {
        var order = Round(30m, 10m);
        order.Status = OrderStatus.Cancelled;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 10m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var act = () => AccountDebtProjection.Project([order], new("CHF"), [],
            [new(OrderId, FirstItem, 1, 1, 1000)]);
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void OneCentDebtRemainsCollectableWithoutApplyingTheLegacyTolerance()
    {
        var result = AccountDebtProjection.Project([Round(0.01m, 0)], new("CHF"), [], []);
        result.OutstandingMinor.Should().Be(1);
        result.AvailableMinor.Should().Be(1);
    }

    [Fact]
    public void Voided_unit_uses_the_original_discounted_line_share_without_redistributing_it()
    {
        var order = DiscountedMultiUnitRound();
        order.BillingCreditAmount = 10.01m;
        var amendment = Amendment(order.Id, RangeChange(SecondItem, OrderAmendmentChangeKind.Void, 1, 1));

        var result = AccountDebtProjection.Project([order], new("CHF"), [], [], [amendment]);

        result.OutstandingMinor.Should().Be(3000);
        result.Outstanding.Should().Equal(
            new AccountDebtSegment(OrderId, FirstItem, 1, 1, 1000),
            new AccountDebtSegment(OrderId, SecondItem, 2, 2, 1000));
    }

    [Fact]
    public void Replacement_addition_is_counted_once_as_its_supplement_order()
    {
        var source = DiscountedMultiUnitRound();
        source.BillingCreditAmount = 10.01m;
        var supplementId = Guid.NewGuid();
        var supplementItemId = Guid.NewGuid();
        var supplement = new Order
        {
            Id = supplementId,
            Total = 12m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch,
            Items = [new OrderItem { Id = supplementItemId, OrderId = supplementId,
                Quantity = 1, ItemTotal = 12m, CreatedBy = "test" }]
        };
        var change = RangeChange(SecondItem, OrderAmendmentChangeKind.Replace, 1, 1,
            new OrderItemDto { Id = supplementItemId, Quantity = 1 });
        var amendment = Amendment(source.Id, change, supplementId);

        var result = AccountDebtProjection.Project([source, supplement], new("CHF"), [], [], [amendment]);

        result.OutstandingMinor.Should().Be(4200, "the old unit loses its frozen 10.01 share and the linked new round adds 12.00 once");
        result.Outstanding.Should().ContainSingle(value => value.OrderId == supplementId && value.TotalMinor == 1200);
    }

    [Fact]
    public void Instruction_changes_leave_frozen_account_debt_unchanged()
    {
        var order = new Order
        {
            Id = OrderId,
            Total = 30.01m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch,
            Items = [new OrderItem { Id = FirstItem, OrderId = OrderId,
                Quantity = 3, ItemTotal = 30.01m, CreatedBy = "test" }]
        };
        var previous = new OrderItemDto { Id = FirstItem, Quantity = 3 };
        var amendment = Amendment(order.Id, new OrderAmendmentChangeSnapshot(
            FirstItem, OrderAmendmentChangeKind.InstructionChange, 0, 0, true,
            previous, previous with { SpecialInstructions = "No onions" }));

        var result = AccountDebtProjection.Project([order], new("CHF"), [], [], [amendment]);

        result.OutstandingMinor.Should().Be(3001);
        result.Outstanding.Should().Equal(new AccountDebtSegment(OrderId, FirstItem, 1, 1, 1001),
            new AccountDebtSegment(OrderId, FirstItem, 2, 2, 1000));
    }

    [Fact]
    public void Historical_payment_prefix_is_allocated_before_the_voided_ordinal_is_removed()
    {
        var order = new Order
        {
            Id = OrderId,
            Total = 30.01m,
            TotalPaid = 5m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch,
            Payments = [new OrderPayment { Id = Guid.NewGuid(), OrderId = OrderId,
                Amount = 5m, Status = PaymentStatus.Completed, CreatedBy = "test" }],
            Items = [new OrderItem { Id = FirstItem, OrderId = OrderId,
                Quantity = 3, ItemTotal = 30.01m, CreatedBy = "test" }]
        };
        order.BillingCreditAmount = 10m;
        var amendment = Amendment(order.Id, RangeChange(FirstItem, OrderAmendmentChangeKind.Void, 2, 1));

        var result = AccountDebtProjection.Project([order], new("CHF"), [], [], [amendment]);

        result.OutstandingMinor.Should().Be(1501);
        result.Outstanding.Should().Equal(
            new AccountDebtSegment(OrderId, FirstItem, 1, 1, 501),
            new AccountDebtSegment(OrderId, FirstItem, 3, 1, 1000));
    }

    [Fact]
    public void Captured_and_reserved_allocations_on_unrelated_units_survive_a_void()
    {
        var order = new Order
        {
            Id = OrderId,
            Total = 30.01m,
            TotalPaid = 10m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch,
            Payments = [new OrderPayment { Id = Guid.NewGuid(), OrderId = OrderId,
                Amount = 10m, Status = PaymentStatus.Completed, CreatedBy = "test" }],
            Items = [new OrderItem { Id = FirstItem, OrderId = OrderId,
                Quantity = 3, ItemTotal = 30.01m, CreatedBy = "test" }]
        };
        order.BillingCreditAmount = 10.01m;
        var amendment = Amendment(order.Id, RangeChange(FirstItem, OrderAmendmentChangeKind.Void, 1, 1));

        var result = AccountDebtProjection.Project([order], new("CHF"),
            [new(OrderId, FirstItem, 3, 1, 1000)], [new(OrderId, FirstItem, 2, 1, 400)], [amendment]);

        result.OutstandingMinor.Should().Be(1000);
        result.ReservedMinor.Should().Be(400);
        result.Available.Should().Equal(new AccountDebtSegment(OrderId, FirstItem, 2, 1, 600));
    }

    [Fact]
    public void Allocations_for_a_voided_unit_fail_closed()
    {
        var order = new Order
        {
            Id = OrderId,
            Total = 30.01m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch,
            Items = [new OrderItem { Id = FirstItem, OrderId = OrderId,
                Quantity = 3, ItemTotal = 30.01m, CreatedBy = "test" }]
        };
        var amendment = Amendment(order.Id, RangeChange(FirstItem, OrderAmendmentChangeKind.Void, 1, 1));
        var project = () => AccountDebtProjection.Project([order], new("CHF"),
            [new(OrderId, FirstItem, 1, 1, 1001)], [], [amendment]);

        project.Should().Throw<ConflictException>();
    }

    [Fact]
    public void MixedRefundedAndCapturedTendersRequireReconciliationWithoutErasingTheLiveDebt()
    {
        var order = Round(100m, 20m);
        order.PaymentStatus = PaymentStatus.PartiallyRefunded;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 50m,
            RefundedAmount = 50m,
            Status = PaymentStatus.Refunded,
            CreatedBy = "test"
        });
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 20m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var act = () => AccountDebtProjection.Project([order], new("CHF"), [], []);
        act.Should().Throw<ConflictException>().WithMessage("*refund*reconciliation*");
    }

    [Fact]
    public void ProvenFullRefundWithNoAccountClaimsHasNoCollectableDebt()
    {
        var order = Round(100m, 0);
        order.PaymentStatus = PaymentStatus.Refunded;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 100m,
            RefundedAmount = 100m,
            Status = PaymentStatus.Refunded,
            CreatedBy = "test"
        });
        var result = AccountDebtProjection.Project([order], new("CHF"), [], []);
        result.OutstandingMinor.Should().Be(0);
        result.AvailableMinor.Should().Be(0);
    }

    [Fact]
    public void RefundingAllCapturedMoneyDoesNotProveAnUnderpaidLiveOrderWasVoided()
    {
        var order = Round(100m, 0);
        order.PaymentStatus = PaymentStatus.PartiallyRefunded;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 50m,
            RefundedAmount = 50m,
            Status = PaymentStatus.Refunded,
            CreatedBy = "test"
        });
        var act = () => AccountDebtProjection.Project([order], new("CHF"), [], []);
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void CancelledCapturedMoneyRequiresCreditResolutionBeforeItDisappears()
    {
        var order = Round(100m, 20m);
        order.Status = OrderStatus.Cancelled;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 20m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var act = () => AccountDebtProjection.Project([order], new("CHF"), [], []);
        act.Should().Throw<ConflictException>();
    }

    private static Order Round(decimal total, decimal paid) => new()
    {
        Id = OrderId,
        Total = total,
        TotalPaid = paid,
        Type = OrderType.DineIn,
        Status = OrderStatus.Completed,
        CreatedBy = "test",
        OrderDate = DateTime.UnixEpoch,
        Items = [new() { Id = FirstItem, OrderId = OrderId, Quantity = 1, ItemTotal = 100m, CreatedBy = "test" },
            new() { Id = SecondItem, OrderId = OrderId, Quantity = 2, ItemTotal = 200m, CreatedBy = "test" }]
    };

    private static Order DiscountedMultiUnitRound() => new()
    {
        Id = OrderId,
        Total = 40.01m,
        Type = OrderType.DineIn,
        Status = OrderStatus.Completed,
        CreatedBy = "test",
        OrderDate = DateTime.UnixEpoch,
        Items = [new OrderItem { Id = FirstItem, OrderId = OrderId,
                Quantity = 1, ItemTotal = 10m, CreatedBy = "test" },
            new OrderItem { Id = SecondItem, OrderId = OrderId,
                Quantity = 3, ItemTotal = 30m, CreatedBy = "test" }]
    };

    private static OrderAmendmentChangeSnapshot RangeChange(
        Guid itemId, OrderAmendmentChangeKind kind, int start, int quantity, OrderItemDto? current = null) =>
        new(itemId, kind, start, quantity, false, new OrderItemDto { Id = itemId, Quantity = quantity }, current);

    private static OrderAmendment Amendment(Guid sourceOrderId,
        params OrderAmendmentChangeSnapshot[] changes) => Amendment(sourceOrderId, changes, null);

    private static OrderAmendment Amendment(
        Guid sourceOrderId, OrderAmendmentChangeSnapshot change, Guid? supplementId) =>
        Amendment(sourceOrderId, new[] { change }, supplementId);

    private static OrderAmendment Amendment(Guid sourceOrderId,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes, Guid? supplementId = null) => new()
        {
            Id = Guid.NewGuid(),
            SourceOrderId = sourceOrderId,
            SupplementOrderId = supplementId,
            State = OrderAmendmentState.Committed,
            ChangesJson = OrderAmendmentJson.Serialize(changes),
            CreatedBy = "test"
        };
}
