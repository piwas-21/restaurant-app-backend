using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Api.Features.Payments.Services;
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
[Collection("Database Lane 4")]
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

    [Fact]
    public async Task Refund_OnAStripeSettledTender_IsRefusedAndWritesNothing()
    {
        // S11. Booking this would put a returned amount in the ledger, on the order and in the
        // Z-report against a charge still sitting at Stripe — the platform key carries no refunds
        // write, so nothing here can actually move it (SOFRA-PAYMENTS-PLAN §4).
        var (orderId, paymentId) = await SeedStripeSettledOrderAsync(total: 50m);

        var response = await RefundAsync(orderId, paymentId, amount: 50m);

        response.Success.Should().BeFalse();

        // The reason is in Errors[0], NOT Message — `ApiResponse.Failure` leaves Message at the
        // constant "Operation failed" and the controller serves the whole thing as a 200. That is
        // also where the frontend's `throwServerRefusal` reads it from, so asserting on Message
        // here would pin a field no caller ever sees.
        response.Message.Should().Be("Operation failed");
        response.Errors.Should().ContainSingle()
            .Which.Should().Contain("Stripe", "the refusal must say WHERE the refund is made");

        // The refusal is only worth anything if nothing was written. A message plus a mutated row
        // is the same false ledger with an apology attached.
        await using var ctx = _fixture.CreateContext();
        var payment = await ctx.OrderPayments.SingleAsync(p => p.Id == paymentId);
        var order = await ctx.Orders.SingleAsync(o => o.Id == orderId);

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.IsRefunded.Should().BeFalse();
        payment.RefundedAmount.Should().BeNull();
        payment.RefundDate.Should().BeNull();
        payment.RefundReason.Should().BeNull();
        order.TotalPaid.Should().Be(50m, "no money was given back, so none may leave the total");
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task Refund_OnATillTender_IsStillAllowed()
    {
        // The control. Without it the guard above is satisfied by a handler that refuses every
        // refund, which would be a regression dressed as a fix — the till path is the one this
        // command exists for and it must keep working.
        var (orderId, paymentId) = await SeedPaidOrderAsync(total: 50m);

        var response = await RefundAsync(orderId, paymentId, amount: 50m);

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var payment = await ctx.OrderPayments.SingleAsync(p => p.Id == paymentId);
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.RefundedAmount.Should().Be(50m);
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

    /// <summary>
    /// The same order, but paid through Stripe hosted Checkout.
    /// </summary>
    /// <remarks>
    /// The tender is built by <see cref="OnlineTenderCompletion"/> — the real settle-path writer —
    /// rather than hand-assembled with <c>PaymentGateway = "Stripe"</c>. That is the whole point of
    /// the fixture: <c>TenderCustody</c> keys off the gateway name, and a hand-built row would
    /// certify a shape instead of the chain. Delete the one line in <c>OnlineTenderCompletion</c>
    /// that stamps the name and this test goes red, which is exactly when it should.
    /// </remarks>
    private async Task<(Guid OrderId, Guid PaymentId)> SeedStripeSettledOrderAsync(decimal total)
    {
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var seed = _fixture.CreateContext();

        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SR-{orderId:N}".Substring(0, 12),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = total,
            Total = total,
            TotalPaid = total,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(RefundPaymentCommandHandlerTests),
        };

        var session = new OrderCheckoutSession
        {
            OrderId = orderId,
            SessionId = $"cs_test_{orderId:N}",
            Currency = "chf",
            AmountMinor = (long)(total * 100),
            IdempotencyKey = $"checkout:{orderId}:1",
            ExpiresAt = now.AddMinutes(31),
            ConnectedAccountId = "acct_test",
            Status = CheckoutSessionStatus.Completed,
            CreatedAt = now,
            CreatedBy = nameof(RefundPaymentCommandHandlerTests),
        };

        var tender = OnlineTenderCompletion.Apply(
            order, session, "pi_test_s11", session.AmountMinor, nameof(RefundPaymentCommandHandlerTests), now);

        seed.Orders.Add(order);
        seed.OrderCheckoutSessions.Add(session);

        await seed.SaveChangesAsync();
        return (orderId, tender.Id);
    }
}
