using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public class OrderSettlementEligibilityUnitTests
{
    [Fact]
    public void Completed_unpaid_order_is_collectible()
    {
        OrderSettlementEligibility.CanCollect(NewOrder(OrderStatus.Completed, PaymentStatus.Pending, 0m))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Pending)]
    [InlineData(OrderStatus.Refunded, PaymentStatus.Pending)]
    [InlineData(OrderStatus.Completed, PaymentStatus.Refunded)]
    [InlineData(OrderStatus.Completed, PaymentStatus.Completed)]
    public void Terminal_or_settled_order_is_not_collectible(OrderStatus status, PaymentStatus paymentStatus)
    {
        var totalPaid = paymentStatus == PaymentStatus.Completed ? 10m : 0m;

        OrderSettlementEligibility.CanCollect(NewOrder(status, paymentStatus, totalPaid))
            .Should().BeFalse();
    }

    [Fact]
    public void A_refunded_tender_blocks_collection_even_when_the_order_still_has_a_balance()
    {
        var order = NewOrder(OrderStatus.Completed, PaymentStatus.PartiallyPaid, 2m);
        order.Payments.Add(new OrderPayment
        {
            Amount = 2m,
            Status = PaymentStatus.PartiallyRefunded,
            IsRefunded = false,
            RefundedAmount = 1m,
            CreatedBy = nameof(OrderSettlementEligibilityUnitTests),
        });

        OrderSettlementEligibility.CanCollect(order).Should().BeFalse();
    }

    [Fact]
    public void Operational_predicate_keeps_unfinished_orders_but_excludes_reversals()
    {
        var predicate = OrderSettlementEligibility.OperationalQueuePredicate().Compile();

        predicate(NewOrder(OrderStatus.Preparing, PaymentStatus.Pending, 0m)).Should().BeTrue();
        predicate(NewOrder(OrderStatus.Completed, PaymentStatus.Pending, 0m)).Should().BeTrue();
        predicate(NewOrder(OrderStatus.Cancelled, PaymentStatus.Pending, 0m)).Should().BeFalse();
        predicate(NewOrder(OrderStatus.Completed, PaymentStatus.Refunded, 0m)).Should().BeFalse();
    }

    [Fact]
    public void Operational_predicate_is_translatable_by_the_PostgreSQL_provider()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql()
            .Options;
        using var context = new ApplicationDbContext(options);

        var sql = context.Orders
            .Where(OrderSettlementEligibility.OperationalQueuePredicate())
            .ToQueryString();

        sql.Should().Contain("total_paid");
        sql.Should().Contain("order_payments");

        var tableSearchSql = context.Orders
            .Where(order => order.TableNumber.HasValue && order.TableNumber.Value.ToString().Contains("120"))
            .ToQueryString();

        tableSearchSql.Should().Contain("table_number");
    }

    private static Order NewOrder(OrderStatus status, PaymentStatus paymentStatus, decimal totalPaid) => new()
    {
        OrderNumber = "UNIT-ORDER",
        Status = status,
        PaymentStatus = paymentStatus,
        Total = 10m,
        TotalPaid = totalPaid,
        RemainingAmount = 10m - totalPaid,
        CreatedBy = nameof(OrderSettlementEligibilityUnitTests),
        Payments = new List<OrderPayment>(),
    };
}
