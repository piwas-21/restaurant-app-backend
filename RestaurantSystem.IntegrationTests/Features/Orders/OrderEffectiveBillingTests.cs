using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderEffectiveBillingTests
{
    [Theory]
    [InlineData(OrderType.Takeaway, 10, 0, 0)]
    [InlineData(OrderType.Delivery, 15, 2, 5)]
    [InlineData(OrderType.DineIn, 12, 2, 2)]
    public void Native_and_table_reads_keep_original_charge_and_collect_only_effective_balance(
        OrderType type, int original, int tip, int payable)
    {
        var source = new Order
        {
            Id = Guid.NewGuid(),
            Type = type,
            Total = original,
            Tip = tip,
            DeliveryFee = type == OrderType.Delivery ? 3m : 0m,
            BillingCreditAmount = 10m,
            RemainingAmount = original,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            CreatedBy = "test"
        };
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_billing_model;Username=test;Password=test").Options);
        var mapper = new OrderMappingService(context, Mock.Of<IOrderDisplayCurrencyResolver>(),
            NullLogger<OrderMappingService>.Instance);
        var detail = mapper.MapToOrderDto(source);
        var summary = mapper.MapToOrderSummaryDto(source);

        detail.Total.Should().Be(original);
        detail.PayableTotal.Should().Be(payable);
        detail.BillingCreditAmount.Should().Be(10m);
        detail.RemainingAmount.Should().Be(payable, "a stale legacy cache cannot authorize charging original food again");
        detail.IsFullyPaid.Should().Be(payable == 0);
        source.IsFullyPaid.Should().Be(payable == 0);
        summary.Total.Should().Be(original);
        summary.PayableTotal.Should().Be(payable);
        OrderSettlementEligibility.Outstanding(source).Should().Be(payable);
        OrderSettlementEligibility.CanCollect(source).Should().Be(payable > 0);
        if (payable == 0)
        {
            Action validate = () => OnlinePaymentEligibility.EnsurePayable(source);
            validate.Should().Throw<BadRequestException>();
        }
        else
            CheckoutAmount.From(source.PayableTotal, "CHF").Minor.Should().Be(payable * 100L);
    }

    [Fact]
    public void Missing_materialized_credit_is_a_reconciliation_error_not_a_second_bill()
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var source = new Order
        {
            Id = orderId,
            Total = 12m,
            Tip = 2m,
            CreatedBy = "test",
            Items = [new OrderItem { Id = itemId, OrderId = orderId, Quantity = 1,
                ItemTotal = 10m, CreatedBy = "test" }]
        };
        var change = new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void,
            1, 1, false, new OrderItemDto { Id = itemId, Quantity = 1 }, null);
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = orderId,
            State = OrderAmendmentState.Committed,
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            CreatedBy = "test"
        };

        var project = () => AccountDebtProjection.Project([source], new("CHF"), [], [], [amendment]);
        project.Should().Throw<ConflictException>().WithMessage("*effective order charge differ*");
        source.BillingCreditAmount = 10m;
        project().OutstandingMinor.Should().Be(200);
    }

    [Fact]
    public async Task A_failed_credit_stage_cannot_publish_financial_resolution()
    {
        var source = new Order { CreatedBy = "test" };
        var amendment = new OrderAmendment { FinancialResolutionJson = "original quoted evidence", CreatedBy = "test" };
        var preview = new OrderAmendmentFinancialPreviewDto("CHF", 0, 1000, -1000, 1000,
            OrderAmendmentFinancialResolutionStatus.Pending, OrderAmendmentCreditState.BalanceReduction,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None);
        var billing = new Mock<IOrderBillingAdjustmentWriter>(MockBehavior.Strict);
        var resolved = preview with { ResolutionStatus = OrderAmendmentFinancialResolutionStatus.Resolved };
        billing.Setup(value => value.StageUnpaidCreditAsync(source, amendment, resolved, CancellationToken.None))
            .ThrowsAsync(new ConflictException("Unresolved ledger evidence."));
        var financial = new OrderAmendmentFinancialResolutionService(Mock.Of<IOrderDisplayCurrencyResolver>(), billing.Object);

        var stage = () => financial.StageAsync(amendment, source, preview, CancellationToken.None);
        await stage.Should().ThrowAsync<ConflictException>();
        amendment.FinancialResolutionJson.Should().Be("original quoted evidence");
        billing.VerifyAll();
    }
}
