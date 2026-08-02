using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Handler-focused integration tests for <see cref="RefundPaymentCommandHandler"/>,
/// driven directly against a real <see cref="ApplicationDbContext"/>.
///
/// The handler had no coverage at all before issue #286, which is how a partial
/// refund came to store an order-level status word on a payment record and to
/// drive <c>Order.TotalPaid</c> negative. Both are pinned here.
/// </summary>
[Collection("Database")]
public class RefundPaymentCommandHandlerTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public RefundPaymentCommandHandlerTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PartialRefund_StoresPartiallyRefundedNotPartiallyPaid()
    {
        // PartiallyPaid is an ORDER-level word — "some tenders in, balance
        // outstanding". Writing it to a single payment made the payment
        // invisible to the Z-report, which filters on PartiallyRefunded.
        var (orderId, paymentId) = await SeedPaidOrderAsync(total: 50m);

        var response = await RefundAsync(orderId, paymentId, amount: 20m);

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var payment = await ctx.OrderPayments.SingleAsync(p => p.Id == paymentId);

        payment.Status.Should().Be(PaymentStatus.PartiallyRefunded);
        payment.Status.Should().NotBe(PaymentStatus.PartiallyPaid,
            "PartiallyPaid describes an order's balance, never a single tender");
        payment.RefundedAmount.Should().Be(20m);
        payment.IsRefunded.Should().BeFalse("IsRefunded means the FULL amount went back");
    }

    [Fact]
    public async Task PartialRefund_LeavesTotalPaidAtWhatTheTillStillHolds()
    {
        // The regression this pins: TotalPaid summed only Completed payments
        // and THEN subtracted refunds. A refund moves the payment out of
        // Completed, so its amount was subtracted without ever being added —
        // a CHF 20 refund on a paid CHF 50 order produced TotalPaid = -20,
        // RemainingAmount = 70, and an order that read as unpaid.
        var (orderId, paymentId) = await SeedPaidOrderAsync(total: 50m);

        await RefundAsync(orderId, paymentId, amount: 20m);

        await using var ctx = _fixture.CreateContext();
        var order = await ctx.Orders.SingleAsync(o => o.Id == orderId);

        order.TotalPaid.Should().Be(30m, "CHF 50 taken, CHF 20 given back");
        order.RemainingAmount.Should().Be(20m);
        order.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid,
            "the ORDER is now partially paid — this is where that word belongs");
    }

    [Fact]
    public async Task FullRefund_ZeroesTotalPaidRatherThanNegatingIt()
    {
        // Same double-subtraction, masked on this path: the all-Refunded
        // branch set PaymentStatus correctly while TotalPaid still read -50.
        var (orderId, paymentId) = await SeedPaidOrderAsync(total: 50m);

        await RefundAsync(orderId, paymentId, amount: 50m);

        await using var ctx = _fixture.CreateContext();
        var order = await ctx.Orders.SingleAsync(o => o.Id == orderId);
        var payment = await ctx.OrderPayments.SingleAsync(p => p.Id == paymentId);

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.IsRefunded.Should().BeTrue();
        order.TotalPaid.Should().Be(0m, "everything taken was given back — not -50");
        order.PaymentStatus.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task PartialRefund_ThenZReport_ReachesTheReportAtAll()
    {
        // THE seam test. Issue #286 was not a bug inside either handler — each
        // was self-consistent. It was the two DISAGREEING about which status
        // means "partially refunded", and no test ran one after the other, so
        // nothing could see the disagreement. This one does: refund through the
        // real command, then read the real report.
        var reportDate = new DateOnly(2026, 5, 2);
        var orderDate = reportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12);
        var (orderId, paymentId) = await SeedPaidOrderAsync(total: 50m, orderDateUtc: orderDate);

        await RefundAsync(orderId, paymentId, amount: 20m);

        await using var ctx = _fixture.CreateContext();
        var report = await new GetZReportQueryHandler(ctx, NullLogger<GetZReportQueryHandler>.Instance)
            .Handle(new GetZReportQuery(reportDate), CancellationToken.None);

        report.Data!.Refunds.RefundCount.Should().Be(1);
        report.Data.Refunds.TotalRefundedAmount.Should().Be(20m);
        report.Data.PaymentsByMethod.Should().ContainSingle("the CHF 50 charge is still a card transaction")
            .Which.TotalAmount.Should().Be(50m);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<RestaurantSystem.Api.Common.Models.ApiResponse<RestaurantSystem.Api.Features.Orders.Dtos.OrderPaymentDto>>
        RefundAsync(Guid orderId, Guid paymentId, decimal amount)
    {
        await using var ctx = _fixture.CreateContext();

        var currentUser = new Mock<ICurrentUserService>();
        var userId = Guid.NewGuid();
        currentUser.Setup(x => x.UserId).Returns(userId);
        // Default-interface methods aren't invoked by Moq; stub explicitly.
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(userId.ToString());

        var handler = new RefundPaymentCommandHandler(
            ctx,
            currentUser.Object,
            NullLogger<RefundPaymentCommandHandler>.Instance);

        return await handler.Handle(
            new RefundPaymentCommand
            {
                OrderId = orderId,
                PaymentId = paymentId,
                RefundAmount = amount,
                RefundReason = "Test refund",
            },
            CancellationToken.None);
    }

    /// <summary>
    /// An order fully settled by one completed card payment — the state a
    /// refund is only ever issued against (the handler refuses anything that
    /// is not <see cref="PaymentStatus.Completed"/>).
    /// </summary>
    private async Task<(Guid OrderId, Guid PaymentId)> SeedPaidOrderAsync(decimal total, DateTime? orderDateUtc = null)
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await using var seed = _fixture.CreateContext();
        seed.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"RF-{orderId:N}".Substring(0, 12),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = total,
            Total = total,
            TotalPaid = total,
            RemainingAmount = 0m,
            OrderDate = orderDateUtc ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(RefundPaymentCommandHandlerTests),
            Payments =
            {
                new OrderPayment
                {
                    Id = paymentId,
                    OrderId = orderId,
                    PaymentMethod = PaymentMethod.CreditCard,
                    Amount = total,
                    Status = PaymentStatus.Completed,
                    PaymentDate = DateTime.UtcNow,
                    IsRefunded = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = nameof(RefundPaymentCommandHandlerTests),
                },
            },
        });

        await seed.SaveChangesAsync();
        return (orderId, paymentId);
    }
}
