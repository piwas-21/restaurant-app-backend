using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Handler-focused integration tests for <see cref="CancelOrderCommandHandler"/>.
///
/// <para>
/// The handler had no coverage at all, which is how its refund loop came to book a full refund for
/// every completed tender on the order without any gateway ever being called (S11). For a Stripe
/// capture that is a ledger saying the diner got their money back while the charge is still sitting
/// at Stripe.
/// </para>
/// </summary>
[Collection("Database")]
public class CancelOrderCommandHandlerTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public CancelOrderCommandHandlerTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Cancel_LeavesAStripeTenderUnrefunded()
    {
        var orderId = await SeedOrderAsync(stripeTender: 40m);

        var response = await CancelAsync(orderId, "Kitchen closed early");

        response.Success.Should().BeTrue("staff must still be able to close a real service record");

        await using var ctx = _fixture.CreateContext();
        var payment = await ctx.OrderPayments.SingleAsync(p => p.OrderId == orderId);

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.IsRefunded.Should().BeFalse();
        payment.RefundedAmount.Should().BeNull(
            "nothing called Stripe, so nothing may claim the money came back");
        payment.RefundDate.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_StillRefundsATillTender()
    {
        // The control: the loop must keep doing its job for money the restaurant actually holds.
        // Without this, "skip Stripe tenders" is satisfied by skipping everything.
        var orderId = await SeedOrderAsync(cashTender: 40m);

        await CancelAsync(orderId, "Guest left");

        await using var ctx = _fixture.CreateContext();
        var payment = await ctx.OrderPayments.SingleAsync(p => p.OrderId == orderId);

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.IsRefunded.Should().BeTrue();
        payment.RefundedAmount.Should().Be(40m);
    }

    [Fact]
    public async Task Cancel_RefundsTheTillTenderAndSkipsTheStripeOneOnTheSameOrder()
    {
        // A part-online, part-till order is the case a blanket skip and a blanket refund both get
        // wrong, and it is reachable today: a diner pays online, then adds a drink at the till.
        var orderId = await SeedOrderAsync(stripeTender: 40m, cashTender: 5m);

        await CancelAsync(orderId, "Wrong table");

        await using var ctx = _fixture.CreateContext();
        var payments = await ctx.OrderPayments.Where(p => p.OrderId == orderId).ToListAsync();

        payments.Single(p => p.PaymentMethod == PaymentMethod.Cash)
            .RefundedAmount.Should().Be(5m);
        payments.Single(p => p.PaymentMethod == PaymentMethod.OnlinePayment)
            .RefundedAmount.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_RecordsTheUnrefundedAmountOnTheOrderTimeline()
    {
        // The log line reaches whoever reads logs. This reaches the staff member who opens the
        // order tomorrow and asks why the diner was never paid back — an invisible debt is the
        // failure mode that makes skipping worse than booking a false refund.
        var orderId = await SeedOrderAsync(stripeTender: 40m);

        await CancelAsync(orderId, "Kitchen closed early");

        await using var ctx = _fixture.CreateContext();
        var history = await ctx.OrderStatusHistories
            .SingleAsync(h => h.OrderId == orderId && h.ToStatus == OrderStatus.Cancelled);

        history.Notes.Should().Contain("Kitchen closed early");
        history.Notes.Should().Contain("NOT REFUNDED");
        history.Notes.Should().Contain("Stripe");
        history.Notes.Should().Contain("40");
    }

    [Fact]
    public async Task Cancel_WithATillTenderOnly_AddsNoOutstandingRefundNote()
    {
        // A note that appears every time carries no information, and "NOT REFUNDED" on an order
        // that WAS refunded is worse than silence.
        var orderId = await SeedOrderAsync(cashTender: 40m);

        await CancelAsync(orderId, "Guest left");

        await using var ctx = _fixture.CreateContext();
        var history = await ctx.OrderStatusHistories
            .SingleAsync(h => h.OrderId == orderId && h.ToStatus == OrderStatus.Cancelled);

        history.Notes.Should().Be("Cancellation reason: Guest left");
    }

    [Fact]
    public async Task Cancel_WithAReasonThatFillsItsColumn_StillSavesAndKeepsTheWarning()
    {
        // `Notes` is varchar(500) and it holds the reason PLUS the warning, so a reason that is
        // itself legal at its own column's 500 already overflows the note. Appending unguarded
        // would have turned a working cancellation into a 500 — on exactly the orders where the
        // warning matters most. The reason is trimmed around the warning, not the other way round.
        //
        // 500 rather than something longer because `Order.CancellationReason` is varchar(500) with
        // NO validator maximum, so a longer reason 500s on a different column entirely — a
        // pre-existing defect this slice does not touch (backend issue filed).
        var orderId = await SeedOrderAsync(stripeTender: 40m);

        var response = await CancelAsync(orderId, new string('x', 500));

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var history = await ctx.OrderStatusHistories
            .SingleAsync(h => h.OrderId == orderId && h.ToStatus == OrderStatus.Cancelled);

        history.Notes!.Length.Should().BeLessThanOrEqualTo(500);
        history.Notes.Should().Contain("NOT REFUNDED", "the warning is the part that must survive");
    }

    [Fact]
    public async Task Cancel_WithAnEmojiReason_DoesNotDieCuttingACharacterInHalf()
    {
        // ASCII is the ONE input class for which slicing by UTF-16 unit is safe, so a test that
        // only ever passes 'x' certifies nothing about the clamp. Cutting between the halves of an
        // astral character leaves a lone high surrogate, and Npgsql's UTF-8 encoder THROWS on one:
        // the save dies and the cancellation is lost. Truncating is new in this slice, so this
        // failure would have been new too — and it lands on precisely the orders that carry a
        // gateway warning, because the warning is what makes the reason need trimming at all.
        var orderId = await SeedOrderAsync(stripeTender: 40m);

        var response = await CancelAsync(orderId, string.Concat(Enumerable.Repeat("\U0001F600", 250)));

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var history = await ctx.OrderStatusHistories
            .SingleAsync(h => h.OrderId == orderId && h.ToStatus == OrderStatus.Cancelled);

        history.Notes!.Length.Should().BeLessThanOrEqualTo(500);
        history.Notes.Should().Contain("NOT REFUNDED");

        // Asserted across the WHOLE string, not on its last character: the suffix follows the
        // trimmed reason, so a split pair sits in the middle and an end-of-string check would pass
        // no matter what. (The save succeeding is already strong evidence — Npgsql refuses to
        // encode a lone surrogate — but that makes the reason explicit rather than incidental.)
        new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true)
            .Invoking(e => e.GetByteCount(history.Notes))
            .Should().NotThrow("a split surrogate pair is unencodable");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<RestaurantSystem.Api.Common.Models.ApiResponse<OrderDto>> CancelAsync(
        Guid orderId, string reason)
    {
        await using var ctx = _fixture.CreateContext();

        var currentUser = new Mock<ICurrentUserService>();
        var userId = Guid.NewGuid();
        currentUser.Setup(x => x.UserId).Returns(userId);
        // Default-interface methods aren't invoked by Moq; stub explicitly.
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(userId.ToString());

        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(m => m.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderDto());

        var handler = new CancelOrderCommandHandler(
            ctx,
            currentUser.Object,
            mapping.Object,
            new Mock<IEmailService>().Object,
            NullLogger<CancelOrderCommandHandler>.Instance);

        return await handler.Handle(
            new CancelOrderCommand { OrderId = orderId, CancellationReason = reason },
            CancellationToken.None);
    }

    /// <summary>
    /// A confirmed order carrying either or both kinds of completed tender.
    /// </summary>
    /// <remarks>
    /// The online tender is written by <see cref="OnlineTenderCompletion"/>, the real settle-path
    /// writer, rather than hand-stamped with <c>PaymentGateway = "Stripe"</c>. <c>TenderCustody</c>
    /// reads the gateway name that writer sets, so a hand-built row would pin a shape this test
    /// invented instead of the chain that actually runs in production.
    /// </remarks>
    private async Task<Guid> SeedOrderAsync(decimal? stripeTender = null, decimal? cashTender = null)
    {
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var total = (stripeTender ?? 0m) + (cashTender ?? 0m);

        await using var seed = _fixture.CreateContext();

        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"CN-{orderId:N}".Substring(0, 12),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = total,
            Total = total,
            TotalPaid = total,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(CancelOrderCommandHandlerTests),
        };

        if (cashTender.HasValue)
        {
            order.Payments.Add(new OrderPayment
            {
                OrderId = orderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = cashTender.Value,
                Status = PaymentStatus.Completed,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = nameof(CancelOrderCommandHandlerTests),
            });
        }

        seed.Orders.Add(order);

        if (stripeTender.HasValue)
        {
            var session = new OrderCheckoutSession
            {
                OrderId = orderId,
                SessionId = $"cs_test_{orderId:N}",
                Currency = "chf",
                AmountMinor = (long)(stripeTender.Value * 100),
                IdempotencyKey = $"checkout:{orderId}:1",
                ExpiresAt = now.AddMinutes(31),
                ConnectedAccountId = "acct_test",
                Status = CheckoutSessionStatus.Completed,
                CreatedAt = now,
                CreatedBy = nameof(CancelOrderCommandHandlerTests),
            };

            OnlineTenderCompletion.Apply(
                order, session, "pi_test_s11", session.AmountMinor, nameof(CancelOrderCommandHandlerTests), now);

            seed.OrderCheckoutSessions.Add(session);
        }

        await seed.SaveChangesAsync();
        return orderId;
    }
}
