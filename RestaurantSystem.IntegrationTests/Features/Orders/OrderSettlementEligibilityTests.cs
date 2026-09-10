using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The staff payment endpoint settles a balance, not a fulfilment lifecycle state.
/// These tests cross the HTTP handler, the registered applicator and PostgreSQL so a response
/// refusal alone cannot hide a tender that was written before the rule ran.
/// </summary>
[Collection("Database Lane 3")]
public class OrderSettlementEligibilityTests : IntegrationTestBase
{
    private Guid _completedUnpaidOrderId;
    private Guid _cancelledOrderId;
    private Guid _fullyRefundedOrderId;
    private Guid _overpaidOrderId;

    public OrderSettlementEligibilityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_completed_but_unpaid_order_can_be_collected_at_the_till()
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync(
            $"/api/Orders/{_completedUnpaidOrderId}/payments",
            new { paymentMethod = "Cash", amount = 10m });

        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeTrue("fulfilment completion does not mean the diner paid");

        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(o => o.Payments)
            .SingleAsync(o => o.Id == _completedUnpaidOrderId);

        order.TotalPaid.Should().Be(10m);
        order.RemainingAmount.Should().Be(0m);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
        order.Payments.Should().ContainSingle();
        order.Payments.Single().PaymentGateway.Should().BeNull(
            "the ordinary till tender remains in restaurant custody");
    }

    [Fact]
    public async Task A_cancelled_order_cannot_be_collected()
    {
        await AssertRefusedWithoutTenderAsync(_cancelledOrderId, expectedPaymentCount: 0);
    }

    [Fact]
    public async Task A_fully_refunded_order_cannot_be_collected()
    {
        await AssertRefusedWithoutTenderAsync(_fullyRefundedOrderId, expectedPaymentCount: 1);
    }

    [Fact]
    public async Task An_overpaid_credit_order_cannot_be_collected()
    {
        await AssertRefusedWithoutTenderAsync(_overpaidOrderId, expectedPaymentCount: 1);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        _completedUnpaidOrderId = Guid.NewGuid();
        _cancelledOrderId = Guid.NewGuid();
        _fullyRefundedOrderId = Guid.NewGuid();
        _overpaidOrderId = Guid.NewGuid();

        context.Orders.AddRange(
            NewOrder(_completedUnpaidOrderId, "C06-COMPLETE", OrderStatus.Completed, PaymentStatus.Pending, 10m, 0m),
            NewOrder(_cancelledOrderId, "C06-CANCELLED", OrderStatus.Cancelled, PaymentStatus.Pending, 10m, 0m),
            NewOrder(_fullyRefundedOrderId, "C06-REFUNDED", OrderStatus.Confirmed, PaymentStatus.Refunded, 10m, 0m),
            NewOrder(_overpaidOrderId, "C06-CREDIT", OrderStatus.Confirmed, PaymentStatus.Overpaid, 10m, 12m));

        context.OrderPayments.AddRange(
            new OrderPayment
            {
                OrderId = _fullyRefundedOrderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 10m,
                Status = PaymentStatus.Refunded,
                IsRefunded = true,
                RefundedAmount = 10m,
                RefundDate = now,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = nameof(OrderSettlementEligibilityTests),
            },
            new OrderPayment
            {
                OrderId = _overpaidOrderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 12m,
                Status = PaymentStatus.Completed,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = nameof(OrderSettlementEligibilityTests),
            });

        await context.SaveChangesAsync();
    }

    private async Task AssertRefusedWithoutTenderAsync(Guid orderId, int expectedPaymentCount)
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync(
            $"/api/Orders/{orderId}/payments",
            new { paymentMethod = "Cash", amount = 1m });

        response.IsSuccessStatusCode.Should().BeTrue("the endpoint represents business refusals in its response body");
        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeFalse();

        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
        order.Payments.Should().HaveCount(expectedPaymentCount,
            "a refused settlement must not place a tender in the ledger");
    }

    private static Order NewOrder(
        Guid id,
        string number,
        OrderStatus status,
        PaymentStatus paymentStatus,
        decimal total,
        decimal totalPaid) => new()
        {
            Id = id,
            OrderNumber = number,
            Type = OrderType.Takeaway,
            Status = status,
            PaymentStatus = paymentStatus,
            SubTotal = total,
            Total = total,
            TotalPaid = totalPaid,
            RemainingAmount = total - totalPaid,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderSettlementEligibilityTests),
        };
}
