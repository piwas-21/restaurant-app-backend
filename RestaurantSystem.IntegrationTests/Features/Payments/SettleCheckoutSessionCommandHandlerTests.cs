using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S5 (SOFRA-PAYMENTS-PLAN §5 slice 5) — the settle path.
///
/// <para>
/// Driven against a real database with Stripe faked, because every property here is about what gets
/// WRITTEN once Stripe has spoken, and because the idempotency claim is a conditional UPDATE that
/// only a real database evaluates. The real <see cref="CheckoutSettlementWriter"/> is used
/// throughout — stubbing it would delete the half of the slice that touches money.
/// </para>
/// </summary>
[Collection("Database")]
public class SettleCheckoutSessionCommandHandlerTests : IAsyncLifetime
{
    private const string ConnectedAccount = "acct_test_connected";
    private const string PaymentIntent = "pi_test_settled";

    private readonly DatabaseFixture _fixture;

    public SettleCheckoutSessionCommandHandlerTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The headline: Stripe says complete and paid, and the money lands in our ledger exactly once —
    /// on the tender order creation already minted, not a second one beside it.
    /// </summary>
    [Fact]
    public async Task A_completed_session_completes_the_tender_it_already_had()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await HandleAsync(seeded.SessionId, StripeSays("complete", "paid", 4250));

        await using var verify = _fixture.CreateContext();
        var payment = await verify.OrderPayments.AsNoTracking().SingleAsync();
        var order = await verify.Orders.AsNoTracking().SingleAsync();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.Amount.Should().Be(42.50m);
        payment.TransactionId.Should().Be(PaymentIntent, "the PaymentIntent is the reference for a Stripe tender");
        payment.PaymentGateway.Should().Be("Stripe");
        payment.Currency.Should().Be("chf");

        order.TotalPaid.Should().Be(42.50m);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);

        row.Status.Should().Be(CheckoutSessionStatus.Completed);
        row.PaymentIntentId.Should().Be(PaymentIntent);
        row.AmountReceivedMinor.Should().Be(4250);
        row.OrderPaymentId.Should().Be(payment.Id, "the row must point at the tender it produced");
    }

    /// <summary>
    /// §6b's trap, and the one S4 got wrong first. A delayed-notification method — SEPA, Klarna,
    /// Sofort, all reachable BECAUSE payment methods are chosen dynamically — reaches
    /// <c>status: complete</c> with <c>payment_status</c> still <c>unpaid</c> while the funds clear.
    /// Keyed off <c>paid</c> alone this reads as "not paid yet", and the session would be expired
    /// and a second one minted for money the diner has already committed.
    /// </summary>
    [Fact]
    public async Task A_complete_but_unpaid_session_is_a_payment_in_flight_not_a_failure()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await HandleAsync(seeded.SessionId, StripeSays("complete", "unpaid", 4250));

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        var payment = await verify.OrderPayments.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Completed, "complete is terminal whatever payment_status says");
        payment.Status.Should().Be(PaymentStatus.Completed);
    }

    /// <summary>
    /// The assertion the row's <c>AmountMinor</c> exists for. Stripe is the authority on whether
    /// money moved; our row is the authority on how much was meant to. A disagreement means the two
    /// describe different charges, so no tender may be written from either — the money stays visible
    /// in Stripe's dashboard for a human instead of being silently booked against the wrong order.
    /// </summary>
    [Fact]
    public async Task A_session_that_charged_a_different_amount_is_refused()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await HandleAsync(seeded.SessionId, StripeSays("complete", "paid", amountTotalMinor: 100));

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Failed);
        row.LastError.Should().Contain("100").And.Contain("4250");
        (await verify.OrderPayments.AsNoTracking().AnyAsync(p => p.Status == PaymentStatus.Completed))
            .Should().BeFalse("no tender may be minted from two numbers that disagree");
    }

    /// <summary>
    /// Settlement must be safe to run twice, because it HAS two callers and no webhook to order
    /// them: the diner's return trip and the reconciler can arrive together or in either order.
    /// </summary>
    [Fact]
    public async Task Settling_twice_does_not_pay_the_order_twice()
    {
        var seeded = await SeedAsync(total: 42.50m);
        var stripe = StripeSays("complete", "paid", 4250);

        await HandleAsync(seeded.SessionId, stripe);
        await HandleAsync(seeded.SessionId, stripe);

        await using var verify = _fixture.CreateContext();
        var payments = await verify.OrderPayments.AsNoTracking().ToListAsync();
        var order = await verify.Orders.AsNoTracking().SingleAsync();

        payments.Should().ContainSingle("the second caller must find the claim already taken");
        order.TotalPaid.Should().Be(42.50m);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed, "not Overpaid");
    }

    /// <summary>
    /// The atomic claim, which the test above does NOT reach.
    /// </summary>
    /// <remarks>
    /// A second SEQUENTIAL settle never gets as far as the claim: by then the row is no longer
    /// <c>Created</c> and the handler returns early on that alone. Removing the
    /// <c>WHERE Status = Created</c> condition therefore leaves
    /// <see cref="Settling_twice_does_not_pay_the_order_twice"/> green — verified by mutation, which
    /// is why this case exists.
    ///
    /// <para>
    /// The state that matters is two callers that have BOTH read the row as <c>Created</c> before
    /// either writes — the return trip and the reconciler overlapping, which is the normal case with
    /// no webhook to order them. Each holds its own <c>AsNoTracking</c> snapshot, so passing the same
    /// snapshot to the writer twice is exactly that situation: the database, not the snapshot, has to
    /// be what refuses the second one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Two_callers_that_both_saw_an_unclaimed_session_settle_it_once()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await using var ctx = _fixture.CreateContext();
        var snapshot = await ctx.OrderCheckoutSessions.AsNoTracking()
            .SingleAsync(s => s.SessionId == seeded.SessionId);

        var first = await NewWriter(ctx).SettleAsync(snapshot, PaymentIntent, 4250, CancellationToken.None);
        var second = await NewWriter(ctx).SettleAsync(snapshot, PaymentIntent, 4250, CancellationToken.None);

        first.PaymentStatus.Should().Be(nameof(PaymentStatus.Completed));
        second.PaymentStatus.Should().Be(nameof(PaymentStatus.Completed),
            "the loser must still report the truth, not an error");

        await using var verify = _fixture.CreateContext();
        var payments = await verify.OrderPayments.AsNoTracking().ToListAsync();
        var order = await verify.Orders.AsNoTracking().SingleAsync();

        payments.Should().ContainSingle("the claim, not the caller's stale snapshot, decides who writes");
        order.TotalPaid.Should().Be(42.50m);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed, "not Overpaid");
    }

    /// <summary>
    /// The deferred confirm. Dine-in gives up its creation-time auto-confirm while payment is in
    /// flight, so settling is what finally hands the ticket to the kitchen.
    /// </summary>
    [Fact]
    public async Task Settling_performs_the_confirm_that_creation_deferred()
    {
        var seeded = await SeedAsync(total: 42.50m, type: OrderType.DineIn);

        await HandleAsync(seeded.SessionId, StripeSays("complete", "paid", 4250));

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking().Include(o => o.StatusHistory).SingleAsync();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.StatusHistory.Should().Contain(h => h.ToStatus == OrderStatus.Confirmed,
            "the confirm must leave the same audit trail a cashier's would");
    }

    /// <summary>
    /// The control for the case above. Takeaway and Delivery are Pending until staff accept them
    /// whether or not they were paid online — confirming them on payment would put orders in the
    /// kitchen the restaurant never accepted.
    /// </summary>
    [Fact]
    public async Task Settling_a_takeaway_order_does_not_confirm_it()
    {
        var seeded = await SeedAsync(total: 42.50m, type: OrderType.Takeaway);

        await HandleAsync(seeded.SessionId, StripeSays("complete", "paid", 4250));

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking().SingleAsync();

        order.Status.Should().Be(OrderStatus.Pending);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed, "paid, but not yet accepted");
    }

    /// <summary>
    /// An id the current key cannot see — a live/test key swap, a database restored across
    /// environments. <c>SessionService.GetAsync</c> THROWS on this rather than returning null;
    /// <c>StripeCheckoutClient</c> narrows that to a null for <c>resource_missing</c> only. The row
    /// must be retired so the order is not wedged behind a session nothing can ever read.
    /// </summary>
    [Fact]
    public async Task A_session_stripe_does_not_recognise_is_retired_not_settled()
    {
        var seeded = await SeedAsync(total: 42.50m);

        var stripe = new Mock<IStripeCheckoutClient>();
        stripe.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);

        await HandleAsync(seeded.SessionId, stripe);

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Failed);
        (await verify.OrderPayments.AsNoTracking().AnyAsync(p => p.Status == PaymentStatus.Completed))
            .Should().BeFalse();
    }

    /// <summary>
    /// Still on Stripe's page. Nothing may change — least of all the row's status, since expiry is
    /// what eventually lets the reconciler CANCEL the order.
    /// </summary>
    [Fact]
    public async Task An_open_session_is_left_alone()
    {
        var seeded = await SeedAsync(total: 42.50m);

        var result = await HandleAsync(seeded.SessionId, StripeSays("open", "unpaid", 4250));

        result.Data!.PaymentStatus.Should().Be(nameof(PaymentStatus.Pending));

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Created);
        row.PaymentIntentId.Should().BeNull();
    }

    [Fact]
    public async Task An_expired_session_is_retired()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await HandleAsync(seeded.SessionId, StripeSays("expired", "unpaid", 4250));

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Expired);
        (await verify.OrderPayments.AsNoTracking().AnyAsync(p => p.Status == PaymentStatus.Completed))
            .Should().BeFalse();
    }

    /// <summary>
    /// Only an explicit <c>expired</c> retires a row. A status this code has never seen — Stripe
    /// adding one, or a typo in a fake — must fall through to "ask again later", because retiring
    /// is what leads to the order being cancelled. Written as its own case because the obvious
    /// implementation ("not open and not complete ⇒ expired") passes every other test in this file.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_stripe_status_is_not_treated_as_expired()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await HandleAsync(seeded.SessionId, StripeSays("something_stripe_added_later", "unpaid", 4250));

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        row.Status.Should().Be(CheckoutSessionStatus.Created,
            "guessing towards expiry cancels a live order; guessing the other way costs one more poll");
    }

    /// <summary>
    /// A session id that was never ours. 404 rather than a silent success, so a caller cannot use
    /// this endpoint to probe which ids exist by watching for a different shape of 200.
    /// </summary>
    [Fact]
    public async Task An_unknown_session_id_is_not_found()
    {
        await SeedAsync(total: 42.50m);

        var act = async () => await HandleAsync("cs_never_existed", StripeSays("complete", "paid", 4250));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// The order was paid at the till while the diner was mid-redirect, so the points are already
    /// awarded. The settle claim stops settlement running twice, but it cannot see a DIFFERENT path
    /// having awarded — and the award gate admits <c>Overpaid</c>, which is exactly what this
    /// becomes.
    /// </summary>
    [Fact]
    public async Task Points_already_awarded_at_the_till_are_not_awarded_again()
    {
        var seeded = await SeedAsync(total: 42.50m, withUser: true);

        await using (var till = _fixture.CreateContext())
        {
            till.FidelityPointsTransactions.Add(new FidelityPointsTransaction
            {
                UserId = seeded.UserId!.Value,
                OrderId = seeded.OrderId,
                TransactionType = TransactionType.Earned,
                Points = 42,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(SettleCheckoutSessionCommandHandlerTests),
            });
            await till.SaveChangesAsync();
        }

        var fidelity = new Mock<IOrderFidelityCoordinator>();

        await HandleAsync(seeded.SessionId, StripeSays("complete", "paid", 4250), fidelity);

        fidelity.Verify(
            f => f.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed record Seeded(Guid OrderId, string SessionId, Guid? UserId);

    private async Task<Seeded> SeedAsync(
        decimal total, OrderType type = OrderType.Takeaway, bool withUser = false)
    {
        await using var seed = _fixture.CreateContext();

        Guid? userId = null;
        if (withUser)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"diner-{Guid.NewGuid():N}@example.com",
                Email = $"diner-{Guid.NewGuid():N}@example.com",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(SettleCheckoutSessionCommandHandlerTests),
                RefreshToken = string.Empty,
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Test",
                LastName = "Diner",
                Role = UserRole.Customer,
            };
            seed.Users.Add(user);
            userId = user.Id;
        }

        var order = new Order
        {
            OrderNumber = $"S5-{Guid.NewGuid():N}"[..12],
            Type = type,
            // Pending for both types — that is what an online order looks like at creation.
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            UserId = userId,
            FidelityPointsEarned = withUser ? 42 : 0,
            SubTotal = total,
            Total = total,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(SettleCheckoutSessionCommandHandlerTests),
        };

        // The tender order creation mints. Its presence is the point: settling must COMPLETE this
        // row rather than add a second one beside it.
        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Amount = total,
            Status = PaymentStatus.Processing,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(SettleCheckoutSessionCommandHandlerTests),
        });

        seed.Orders.Add(order);

        // Saved before the session row is built: Order.Id is assigned by EF at save time, not at
        // Add, so reading it earlier yields Guid.Empty and the session's foreign key fails.
        await seed.SaveChangesAsync();

        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        seed.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = sessionId,
            Status = CheckoutSessionStatus.Created,
            Currency = "chf",
            AmountMinor = decimal.ToInt64(total * 100),
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(31),
            ConnectedAccountId = ConnectedAccount,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(SettleCheckoutSessionCommandHandlerTests),
        });

        await seed.SaveChangesAsync();
        return new Seeded(order.Id, sessionId, userId);
    }

    private static Mock<IStripeCheckoutClient> StripeSays(
        string status, string paymentStatus, long? amountTotalMinor)
    {
        var mock = new Mock<IStripeCheckoutClient>();
        mock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new StripeCheckoutSession
            {
                Id = id,
                Url = status == "open" ? $"https://checkout.stripe.com/c/pay/{id}" : null,
                Status = status,
                PaymentStatus = paymentStatus,
                PaymentIntentId = PaymentIntent,
                AmountTotalMinor = amountTotalMinor,
            });

        return mock;
    }

    private static CheckoutSettlementWriter NewWriter(
        ApplicationDbContext ctx, Mock<IOrderFidelityCoordinator>? fidelity = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.GetAuditIdentifier()).Returns("System");

        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(m => m.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderDto());

        return new CheckoutSettlementWriter(
            ctx,
            // The REAL payment builder: TotalPaid/RemainingAmount/PaymentStatus are the money
            // assertions in this file, and a stub would compute them in the test instead.
            new OrderPaymentBuilder(currentUser.Object),
            (fidelity ?? new Mock<IOrderFidelityCoordinator>()).Object,
            new Mock<IOrderNotificationService>().Object,
            new Mock<IOrderEventService>().Object,
            mapping.Object,
            currentUser.Object,
            NullLogger<CheckoutSettlementWriter>.Instance);
    }

    private async Task<ApiResponse<CheckoutSettlementDto>> HandleAsync(
        string sessionId,
        Mock<IStripeCheckoutClient> stripe,
        Mock<IOrderFidelityCoordinator>? fidelity = null)
    {
        await using var ctx = _fixture.CreateContext();

        var handler = new SettleCheckoutSessionCommandHandler(
            ctx, stripe.Object, NewWriter(ctx, fidelity),
            NullLogger<SettleCheckoutSessionCommandHandler>.Instance);

        return await handler.Handle(
            new SettleCheckoutSessionCommand { SessionId = sessionId }, CancellationToken.None);
    }
}
