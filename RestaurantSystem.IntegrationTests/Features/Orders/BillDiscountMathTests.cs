using System.Text.Json;
using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class BillDiscountMathTests
{
    [Fact]
    public async Task Two_ten_euro_units_with_four_euro_discount_credit_eight_each()
    {
        var order = Source(20m, 2);
        Discount(order, 4m, BillDiscountMath.Food(order, new("EUR")));
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, 1, 1).Should().Be(800);
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, 2, 1).Should().Be(800);
        var change = Removal(order, 1, 1);
        var currency = new Mock<IOrderDisplayCurrencyResolver>();
        currency.Setup(value => value.Resolve(order)).Returns("EUR");
        var preview = await new OrderAmendmentFinancialResolutionService(currency.Object,
            Mock.Of<IOrderBillingAdjustmentWriter>()).PreviewAsync(order, [change], null, CancellationToken.None);
        preview.RemovedUnitValueMinor.Should().Be(800);
        order.BillingCreditAmount = 8m;
        var account = AccountDebtProjection.Project([order], new("EUR"), [], [], [Amendment(order, change)]);
        account.OutstandingMinor.Should().Be(800);
        order.BillingCreditAmount = 16m;
        AccountDebtProjection.Project([order], new("EUR"), [], [],
            [Amendment(order, Removal(order, 1, 2))]).OutstandingMinor.Should().Be(0);
    }

    [Fact]
    public void Discount_after_first_void_targets_only_the_retained_unit()
    {
        var order = Source(20m, 2);
        var amendment = Amendment(order, Removal(order, 1, 1));
        order.BillingCreditAmount = 10m;
        var food = AccountDebtAmendmentProjection.ExcludeVoidedUnits([order], BillDiscountMath.Food(order, new("EUR")), [amendment]);
        Discount(order, 2m, food);
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, 2, 1).Should().Be(800);
        AccountDebtProjection.Project([order], new("EUR"), [], [], [amendment]).OutstandingMinor.Should().Be(800);
    }

    [Theory]
    [InlineData(1, 333)]
    [InlineData(2, 332)]
    [InlineData(3, 333)]
    public void Two_cent_discount_over_three_units_conserves_the_independent_cent_oracle(int ordinal, long expected)
    {
        var order = Source(10m, 3);
        Discount(order, .02m, BillDiscountMath.Food(order, new("EUR")));
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, ordinal, 1).Should().Be(expected);
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, 1, 3).Should().Be(998);
    }

    [Fact]
    public void Later_captured_money_is_conserved_against_net_unit_prices()
    {
        var order = Source(10m, 1);
        Discount(order, 2m, BillDiscountMath.Food(order, new("EUR")));
        order.TotalPaid = 6m;
        order.Payments.Add(new OrderPayment { Amount = 6m, Status = PaymentStatus.Completed, CreatedBy = "test" });
        AccountDebtProjection.Project([order], new("EUR"), [], []).OutstandingMinor.Should().Be(200);
        BillDiscountMath.Removed(order, new("EUR"), order.Items.Single().Id, 1, 1).Should().Be(800);
    }

    [Fact]
    public void Fees_tips_and_malformed_discount_allocations_are_never_discountable_food()
    {
        var order = Source(25m, 2);
        order.Tip = 2m;
        order.DeliveryFee = 3m;
        Discount(order, 4m, BillDiscountMath.Food(order, new("EUR")));
        var account = AccountDebtProjection.Project([order], new("EUR"), [], []);
        account.OutstandingMinor.Should().Be(2100);
        account.Outstanding.Where(value => value.OrderItemId is null).Sum(value => value.TotalMinor).Should().Be(500);
        var charge = OrderBillChargeReader.Read(order).Single();
        order.BillChargesJson = JsonSerializer.Serialize(new[] { charge with {
            Allocations = [new(Guid.NewGuid(), 1, 1, 400)] } }, BillChargeRules.JsonOptions);
        Action corrupt = () => BillDiscountMath.Food(order, new("EUR"));
        corrupt.Should().Throw<ConflictException>();
    }

    [Fact]
    public async Task Zero_value_removal_with_awarded_or_redeemed_points_requires_reconciliation()
    {
        var order = Source(10m, 1);
        Discount(order, 10m, BillDiscountMath.Food(order, new("EUR")));
        order.FidelityPointsEarned = 10;
        order.FidelityPointsRedeemed = 5;
        var currency = new Mock<IOrderDisplayCurrencyResolver>();
        currency.Setup(value => value.Resolve(order)).Returns("EUR");
        var financial = new OrderAmendmentFinancialResolutionService(currency.Object, Mock.Of<IOrderBillingAdjustmentWriter>());
        var action = () => financial.PreviewAsync(order, [Removal(order, 1, 1)], null, CancellationToken.None);
        await action.Should().ThrowAsync<ConflictException>().WithMessage("*loyalty reconciliation*");
    }

    [Theory]
    [InlineData(1, .05, .04, .01)]
    [InlineData(2, .04, .05, 0)]
    public void Current_invoice_preserves_ordinal_penny_ownership(int removedOrdinal, decimal credit, decimal payable, decimal retainedDiscount)
    {
        var order = Source(.09m, 2);
        order.SubTotal = .10m;
        order.Items.Single().ItemTotal = .10m;
        order.Discount = .01m;
        order.BillingCreditAmount = credit;
        var dto = new OrderDto
        {
            Currency = "EUR",
            PayableTotal = payable,
            Items = [new OrderItemDto { Id = order.Items.Single().Id, Quantity = 1, ItemTotal = .05m }]
        };
        CurrentStaffTicketAmounts.Apply(order, dto, [Amendment(order, Removal(order, removedOrdinal, 1))]);
        dto.HasPendingBillingCorrection.Should().BeFalse();
        dto.SubTotal.Should().Be(.05m);
        dto.Discount.Should().Be(retainedDiscount);
        (dto.SubTotal - dto.Discount).Should().Be(payable);
        dto.Total.Should().Be(payable);
        order.Discount.Should().Be(.01m);
    }

    [Fact]
    public void Pending_paid_void_holds_invoice_and_keeps_unsettled_financial_amounts_explicit()
    {
        var order = Source(20m, 2);
        order.SubTotal = 20m;
        Discount(order, 4m, BillDiscountMath.Food(order, new("EUR")));
        order.TotalPaid = 8m;
        var amendment = Amendment(order, Removal(order, 1, 1));
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "EUR", 0, 800, -800, 800, OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview, OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.PendingTillRefund));
        var dto = new OrderDto
        {
            Currency = "EUR",
            SubTotal = 20m,
            Total = 20m,
            PayableTotal = 16m,
            BillCharges = [new("discount", "Discount", -4m, "EUR")],
            Items = [new OrderItemDto { Id = order.Items.Single().Id, Quantity = 1, ItemTotal = 10m }]
        };
        CurrentStaffTicketAmounts.Apply(order, dto, [amendment]);
        dto.HasPendingBillingCorrection.Should().BeTrue();
        dto.SubTotal.Should().Be(20m);
        dto.PayableTotal.Should().Be(16m);
        dto.BillCharges.Single().Amount.Should().Be(-4m);
    }

    [Fact]
    public void Current_invoice_uses_the_fee_actually_charged_after_original_sale_discount()
    {
        var order = Source(10m, 1);
        order.SubTotal = 100m;
        order.Items.Single().ItemTotal = 100m;
        order.DeliveryFee = 20m;
        order.Discount = 110m;
        var item = new OrderItemDto { Id = order.Items.Single().Id, Quantity = 1, ItemTotal = 100m };
        var change = new OrderAmendmentChangeSnapshot(item.Id, OrderAmendmentChangeKind.InstructionChange, 0, 0, true, item, item);
        var dto = new OrderDto { Currency = "EUR", Items = [item], DeliveryFee = 20m, PayableTotal = 10m };
        CurrentStaffTicketAmounts.Apply(order, dto, [Amendment(order, change)]);
        dto.HasPendingBillingCorrection.Should().BeFalse();
        dto.DeliveryFee.Should().Be(10m);
        (dto.SubTotal + dto.DeliveryFee - dto.Discount).Should().Be(10m);
        order.DeliveryFee.Should().Be(20m);
        order.Discount.Should().Be(110m);
    }

    [Fact]
    public void Legacy_unitemized_bill_holds_invoice_without_attempting_an_empty_allocation()
    {
        var order = Source(20m, 1);
        order.Items.Clear();
        order.SubTotal = 20m;
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = order.Id,
            State = OrderAmendmentState.Committed,
            ChangesJson = "[]",
            CreatedBy = "test",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto("EUR", 0, 0, 0, 0,
                OrderAmendmentFinancialResolutionStatus.NotRequired, OrderAmendmentCreditState.None,
                OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None))
        };
        var dto = new OrderDto { Currency = "EUR", SubTotal = 20m, Total = 20m, PayableTotal = 20m };
        CurrentStaffTicketAmounts.Apply(order, dto, [amendment]);
        dto.HasPendingBillingCorrection.Should().BeTrue();
        dto.Total.Should().Be(20m);
        dto.SubTotal.Should().Be(20m);
    }

    private static Order Source(decimal total, int quantity)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Total = total,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            CreatedBy = "test",
            OrderDate = DateTime.UnixEpoch
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Quantity = quantity,
            ItemTotal = total,
            CreatedBy = "test"
        });
        return order;
    }

    private static void Discount(Order order, decimal amount, IReadOnlyList<AccountDebtSegment> food)
    {
        var allocations = BillDiscountMath.Allocate(food, new AccountMoney("EUR").ToMinor(amount));
        order.BillChargeAmount = -amount;
        order.BillChargesJson = JsonSerializer.Serialize(new[] { new OrderBillCharge(Guid.NewGuid(), "discount",
            "Discount", -amount, "EUR", DateTime.UtcNow, "test", amount, "Reduce", "Fixed",
            AccountDebtMath.Total(food), allocations) }, BillChargeRules.JsonOptions);
    }

    private static OrderAmendmentChangeSnapshot Removal(Order order, int start, int count) => new(
        order.Items.Single().Id, OrderAmendmentChangeKind.Void, start, count, false,
        new OrderItemDto { Id = order.Items.Single().Id, Quantity = count }, null);
    private static OrderAmendment Amendment(Order order, OrderAmendmentChangeSnapshot change) => new()
    {
        Id = Guid.NewGuid(),
        SourceOrderId = order.Id,
        State = OrderAmendmentState.Committed,
        ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
        FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto("EUR", 0, 0, 0, 0,
            OrderAmendmentFinancialResolutionStatus.Resolved, OrderAmendmentCreditState.BalanceReduction,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None)),
        CreatedBy = "test"
    };
}
