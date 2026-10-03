using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentFinancialResolutionTests
{
    [Fact]
    public async Task Legacy_tax_credit_remains_pending_until_tax_reporting_is_reconciled()
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var billing = new Mock<IOrderBillingAdjustmentWriter>(MockBehavior.Strict);
        var source = new Order
        {
            Id = orderId,
            Total = 11m,
            Tax = 1m,
            CreatedBy = "test",
            ServiceSession = new TableServiceSession { Currency = "CHF", CreatedBy = "test" },
            Items = [new OrderItem { Id = itemId, OrderId = orderId,
                Quantity = 1, ItemTotal = 11m, CreatedBy = "test" }]
        };
        var service = new OrderAmendmentFinancialResolutionService(
            Mock.Of<IOrderDisplayCurrencyResolver>(), billing.Object);
        var change = new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void,
            1, 1, false, new OrderItemDto { Id = itemId, Quantity = 1 }, null);

        var preview = await service.PreviewAsync(source, [change], null, CancellationToken.None);
        await service.StageAsync(new OrderAmendment { CreatedBy = "test" }, source, preview, CancellationToken.None);

        preview.PotentialCreditMinor.Should().Be(1100);
        preview.CreditState.Should().Be(OrderAmendmentCreditState.PendingAllocationReview);
        preview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Pending);
        source.BillingCreditAmount.Should().Be(0);
        billing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unpaid_balance_reduction_is_resolved_when_staged_without_refund_or_loyalty_work()
    {
        var service = new OrderAmendmentFinancialResolutionService(Mock.Of<IOrderDisplayCurrencyResolver>(), Mock.Of<IOrderBillingAdjustmentWriter>());
        var amendment = new OrderAmendment { CreatedBy = "test" };
        var preview = new OrderAmendmentFinancialPreviewDto("CHF", 0, 1000, -1000, 1000,
            OrderAmendmentFinancialResolutionStatus.Pending, OrderAmendmentCreditState.BalanceReduction,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None);

        await service.StageAsync(amendment, new Order { CreatedBy = "test" }, preview, CancellationToken.None);

        OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson)
            .Should().Be(preview with { ResolutionStatus = OrderAmendmentFinancialResolutionStatus.Resolved });
    }

    [Fact]
    public async Task Legacy_paid_summary_without_tender_evidence_remains_pending_for_custodian_review()
    {
        var service = new OrderAmendmentFinancialResolutionService(Mock.Of<IOrderDisplayCurrencyResolver>(), Mock.Of<IOrderBillingAdjustmentWriter>());
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var source = new Order
        {
            Id = orderId,
            Total = 10m,
            TotalPaid = 4m,
            FidelityPointsEarned = 0,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            ServiceSession = new TableServiceSession { Currency = "CHF", CreatedBy = "test" },
            Items = [new OrderItem { Id = itemId, OrderId = orderId,
                Quantity = 1, ItemTotal = 10m, CreatedBy = "test" }]
        };
        var changes = new[]
        {
            new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void, 1, 1,
                false, new OrderItemDto { Id = itemId, Quantity = 1 }, null)
        };

        var preview = await service.PreviewAsync(source, changes, null, CancellationToken.None);
        var amendment = new OrderAmendment { CreatedBy = "test" };
        await service.StageAsync(amendment, source, preview, CancellationToken.None);

        preview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Pending);
        preview.CreditState.Should().Be(OrderAmendmentCreditState.PendingAllocationReview);
        preview.RefundState.Should().Be(OrderAmendmentRefundState.CustodianReviewRequired);
        OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson)
            .Should().Be(preview);
    }

    [Fact]
    public async Task Legacy_paid_summary_with_only_a_non_captured_payment_row_remains_pending()
    {
        var service = new OrderAmendmentFinancialResolutionService(Mock.Of<IOrderDisplayCurrencyResolver>(), Mock.Of<IOrderBillingAdjustmentWriter>());
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var source = new Order
        {
            Id = orderId,
            Total = 10m,
            TotalPaid = 4m,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            CreatedBy = "test",
            ServiceSession = new TableServiceSession { Currency = "CHF", CreatedBy = "test" },
            Payments = [new OrderPayment { Id = Guid.NewGuid(), OrderId = orderId,
                Amount = 4m, Status = PaymentStatus.Pending, CreatedBy = "test" }],
            Items = [new OrderItem { Id = itemId, OrderId = orderId,
                Quantity = 1, ItemTotal = 10m, CreatedBy = "test" }]
        };
        var changes = new[]
        {
            new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void, 1, 1,
                false, new OrderItemDto { Id = itemId, Quantity = 1 }, null)
        };

        var preview = await service.PreviewAsync(source, changes, null, CancellationToken.None);

        preview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Pending);
        preview.CreditState.Should().Be(OrderAmendmentCreditState.PendingAllocationReview);
        preview.RefundState.Should().Be(OrderAmendmentRefundState.CustodianReviewRequired);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"resolutionStatus\":\"NotRequired\",\"creditState\":\"None\",\"loyaltyState\":\"None\",\"refundState\":\"None\"}")]
    public void Missing_financial_snapshot_fields_fail_closed(string json)
    {
        OrderAmendmentFinancialGuard.IsUnresolved(json).Should().BeTrue();
    }

    [Fact]
    public async Task Loyalty_discount_without_point_counters_still_requires_reconciliation()
    {
        var billing = new Mock<IOrderBillingAdjustmentWriter>(MockBehavior.Strict);
        var service = new OrderAmendmentFinancialResolutionService(Mock.Of<IOrderDisplayCurrencyResolver>(), billing.Object);
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var source = new Order
        {
            Id = orderId,
            Total = 9m,
            FidelityPointsDiscount = 1m,
            CreatedBy = "test",
            ServiceSession = new TableServiceSession { Currency = "CHF", CreatedBy = "test" },
            Items = [new OrderItem { Id = itemId, OrderId = orderId, Quantity = 1, ItemTotal = 10m, CreatedBy = "test" }]
        };
        var changes = new[] { new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void, 1, 1,
            false, new OrderItemDto { Id = itemId, Quantity = 1 }, null) };

        var preview = await service.PreviewAsync(source, changes, null, CancellationToken.None);
        var amendment = new OrderAmendment { CreatedBy = "test" };
        await service.StageAsync(amendment, source, preview, CancellationToken.None);

        preview.PotentialCreditMinor.Should().Be(900);
        preview.LoyaltyState.Should().Be(OrderAmendmentLoyaltyState.PendingReview);
        OrderAmendmentFinancialGuard.IsUnresolved(amendment.FinancialResolutionJson).Should().BeTrue();
        source.BillingCreditAmount.Should().Be(0);
        billing.VerifyNoOtherCalls();
    }

    [Fact]
    public void Complete_valid_not_required_snapshot_is_resolved()
    {
        var json = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 0, 0, 0, OrderAmendmentFinancialResolutionStatus.NotRequired,
            OrderAmendmentCreditState.None, OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None));

        OrderAmendmentFinancialGuard.IsUnresolved(json).Should().BeFalse();
    }
}
