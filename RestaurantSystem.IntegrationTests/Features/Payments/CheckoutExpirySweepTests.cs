using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
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
/// S7 (SOFRA-PAYMENTS-PLAN §5 slice 7) — the expiry sweep, which is the one thing in the payments
/// programme that <b>cancels orders</b> (CLAUDE.md §9 data-loss class).
/// </summary>
/// <remarks>
/// Most of this file is about what the sweep must NOT cancel. The destructive branch has one narrow
/// entrance and four guards around it, and a guard that is not fired by a test is a guard nobody has
/// shown to work — so each is driven from the state that would trip it in production: a confirmed
/// order on the pass, a diner who paid cash instead, a second payment already in progress.
/// <para>
/// Stripe is faked; the database is real. Every property asserted here is about what gets written.
/// </para>
/// </remarks>
[Collection("Database")]
public class CheckoutExpirySweepTests : IAsyncLifetime
{
    private const string ConnectedAccount = "acct_test_connected";
    private const string PaymentIntent = "pi_test_swept";

    private readonly DatabaseFixture _fixture;

    public CheckoutExpirySweepTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The closed tab, and the reason the sweep polls sessions that have NOT expired yet. The diner
    /// paid and never came back; nothing but this poll will ever notice.
    /// </summary>
    [Fact]
    public async Task A_session_paid_without_a_return_trip_is_settled_and_the_order_survives()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(StripeSays("complete", "paid", 4250));

        report.Settled.Should().Be(1);
        report.OrdersCancelled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking().SingleAsync();
        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        session.Status.Should().Be(CheckoutSessionStatus.Completed);
        order.Status.Should().Be(OrderStatus.Confirmed, "a paid dine-in order performs the deferred confirm");
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
    }

    /// <summary>The destructive branch itself: nobody paid, and the order is cancelled.</summary>
    [Fact]
    public async Task An_expired_session_cancels_the_order_behind_it()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));

        report.Expired.Should().Be(1);
        report.OrdersCancelled.Should().Be(1);

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking()
            .Include(o => o.StatusHistory)
            .SingleAsync();

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().NotBeNullOrWhiteSpace();

        // The history row is the only durable record of WHY an order the restaurant never touched
        // is suddenly cancelled — without it, staff see a vanished order and no explanation.
        order.StatusHistory.Should().ContainSingle(h => h.ToStatus == OrderStatus.Cancelled)
            .Which.Notes.Should().Contain("checkout session");

        var tender = await verify.OrderPayments.AsNoTracking().SingleAsync();
        tender.Status.Should().Be(PaymentStatus.Failed, "the tender must not be left Processing forever");
    }

    /// <summary>
    /// <b>A session the settle path FAILED must not cancel anything.</b> This is the entrance to the
    /// destructive branch, and getting it wrong is far worse than any guard behind it: the settle
    /// command writes <c>Failed</c> when Stripe cannot be read at all — a live/test key swap, a
    /// rotated connected account, a database restored across environments — which plan §6b says the
    /// reconciler "will hit on every stale row it sweeps". Cancelling on it would destroy every
    /// Pending order carrying a live session within one sweep of a key mix-up.
    /// </summary>
    [Fact]
    public async Task A_session_stripe_cannot_read_fails_without_cancelling_the_order()
    {
        await SeedAsync(total: 42.50m);

        var stripe = new Mock<IStripeCheckoutClient>();
        stripe.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);

        var report = await RunAsync(stripe);

        report.OrdersCancelled.Should().Be(0);
        report.Expired.Should().Be(0, "an unreadable session is not an expired one");

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync()).Status
            .Should().Be(CheckoutSessionStatus.Failed);
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// The other, worse route to <c>Failed</c>: Stripe says the session is complete but for a
    /// different amount than we recorded. The diner <b>has paid</b> — the settle command refuses so
    /// the money stays visible in Stripe's dashboard for a human. Cancelling the order behind it
    /// would destroy the record of a paid order.
    /// </summary>
    [Fact]
    public async Task A_session_whose_amount_disagrees_is_failed_without_cancelling_the_order()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(StripeSays("complete", "paid", amountTotalMinor: 9900));

        report.OrdersCancelled.Should().Be(0);
        report.Settled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync()).Status
            .Should().Be(CheckoutSessionStatus.Failed);
        (await verify.Orders.AsNoTracking().SingleAsync()).Status
            .Should().Be(OrderStatus.Pending, "a diner who paid must not lose their order to a timer");
    }

    /// <summary>
    /// One poison row must not stop the pass. The query is ordered oldest-first, so without
    /// per-row isolation the same head row aborts every sweep forever and nothing behind it — including
    /// sessions a diner has already paid for — is ever reconciled again.
    /// </summary>
    [Fact]
    public async Task A_session_that_throws_does_not_stop_the_rest_of_the_pass()
    {
        var poison = await SeedAsync(total: 10m);
        var healthy = await SeedAsync(total: 42.50m);

        var stripe = new Mock<IStripeCheckoutClient>();
        stripe.Setup(c => c.GetAsync(poison.SessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe is down"));
        stripe.Setup(c => c.GetAsync(healthy.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Remote(healthy.SessionId, "complete", "paid", 4250));

        var report = await RunAsync(stripe);

        report.Failures.Should().Be(1);
        report.Settled.Should().Be(1, "the healthy session behind the poison one must still settle");

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync(s => s.OrderId == healthy.OrderId))
            .Status.Should().Be(CheckoutSessionStatus.Completed);
    }

    /// <summary>
    /// The "only from Pending" rule, driven from the OTHER status the transition table would allow.
    /// Without this, the rule is demonstrated against Confirmed alone.
    /// </summary>
    [Fact]
    public async Task An_order_awaiting_approval_is_not_cancelled()
    {
        await SeedAsync(total: 42.50m, orderStatus: OrderStatus.PendingApproval);

        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));

        report.OrdersCancelled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.PendingApproval);
    }

    /// <summary>
    /// The captured-payment guard lives inside a conditional UPDATE, so its status list had to be
    /// spelled out — <c>IsCaptured()</c> is a C# extension method and does not translate to SQL.
    /// This is the drift alarm: add a member to <c>IsCaptured()</c> and forget the sweep, and an
    /// order paid by that tender starts being cancelled by a timer.
    /// </summary>
    [Fact]
    public void CapturedStatusesMatchIsCaptured()
    {
        var captured = Enum.GetValues<PaymentStatus>().Where(s => s.IsCaptured()).ToArray();

        CheckoutExpirySweep.CapturedStatusesForTests.Should().BeEquivalentTo(captured);
    }

    /// <summary>
    /// The rule the plan states outright: never touch an order that reached Confirmed. The
    /// transition table PERMITS Confirmed → Cancelled — that is right for a human with a reason and
    /// wrong for a timer — so this passes only because the sweep tests the status directly.
    /// </summary>
    [Fact]
    public async Task A_confirmed_order_is_never_cancelled_even_when_its_session_expires()
    {
        await SeedAsync(total: 42.50m, orderStatus: OrderStatus.Confirmed);

        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));

        report.Expired.Should().Be(1, "the session itself is still over");
        report.OrdersCancelled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Confirmed);
    }

    /// <summary>
    /// The likeliest ending for an abandoned Checkout: the diner gave up on the phone and paid cash
    /// at the till. The order is paid and being made; the expiring session says nothing about it.
    /// </summary>
    [Fact]
    public async Task An_order_paid_by_another_tender_is_not_cancelled()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await using (var till = _fixture.CreateContext())
        {
            till.OrderPayments.Add(new OrderPayment
            {
                OrderId = seeded.OrderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 42.50m,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(CheckoutExpirySweepTests),
            });
            await till.SaveChangesAsync();
        }

        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));

        report.OrdersCancelled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync()).Status
            .Should().Be(OrderStatus.Pending, "an order the restaurant has been paid for must survive");
    }

    /// <summary>A second session means a payment may be in progress at this very moment.</summary>
    [Fact]
    public async Task An_order_with_another_live_session_is_not_cancelled()
    {
        var seeded = await SeedAsync(total: 42.50m);

        await using (var second = _fixture.CreateContext())
        {
            second.OrderCheckoutSessions.Add(NewSession(seeded.OrderId, 4250, DateTime.UtcNow.AddMinutes(31)));
            await second.SaveChangesAsync();
        }

        // Only the first session is expired at Stripe; the sweep polls both, so the fake answers
        // per id — the second stays open.
        var stripe = new Mock<IStripeCheckoutClient>();
        stripe.Setup(c => c.GetAsync(seeded.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Remote(seeded.SessionId, "expired", "unpaid", 4250));
        stripe.Setup(c => c.GetAsync(It.Is<string>(id => id != seeded.SessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => Remote(id, "open", "unpaid", 4250));

        var report = await RunAsync(stripe);

        report.OrdersCancelled.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Pending);
    }

    /// <summary>Still on Stripe's page. The sweep must be a no-op, not an early cancel.</summary>
    [Fact]
    public async Task An_open_session_is_left_alone()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(StripeSays("open", "unpaid", 4250));

        report.Should().BeEquivalentTo(new { Examined = 1, Settled = 0, Expired = 0, OrdersCancelled = 0 },
            options => options.ExcludingMissingMembers());

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync()).Status
            .Should().Be(CheckoutSessionStatus.Created);
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// A sweeper that is not idempotent cancels the same order twice, or re-reads Stripe forever.
    /// The second pass must find nothing left to do — which also proves the sweep's own query, not
    /// just its writes, converges.
    /// </summary>
    [Fact]
    public async Task A_second_pass_finds_nothing_to_do()
    {
        await SeedAsync(total: 42.50m);
        var stripe = StripeSays("expired", "unpaid", 4250);

        await RunAsync(stripe);
        var second = await RunAsync(stripe);

        second.Examined.Should().Be(0, "a retired session is no longer Created, so it leaves the query");
        second.Expired.Should().Be(0);
        second.OrdersCancelled.Should().Be(0);
    }

    private async Task<CheckoutExpirySweepReport> RunAsync(Mock<IStripeCheckoutClient> stripe)
    {
        await using var context = _fixture.CreateContext();
        var currentUser = CurrentUser();

        var settleHandler = new SettleCheckoutSessionCommandHandler(
            context,
            stripe.Object,
            new CheckoutSettlementWriter(
                context,
                // The REAL payment builder — TotalPaid and PaymentStatus are assertions in this file.
                new OrderPaymentBuilder(currentUser),
                new Mock<IOrderFidelityCoordinator>().Object,
                new Mock<ISettlementNotifier>().Object,
                currentUser,
                NullLogger<CheckoutSettlementWriter>.Instance),
            new CheckoutSessionRetirement(context, currentUser, NullLogger<CheckoutSessionRetirement>.Instance),
            NullLogger<SettleCheckoutSessionCommandHandler>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<SettleCheckoutSessionCommand, ApiResponse<CheckoutSettlementDto>>>(
            settleHandler);

        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(m => m.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderDto());

        var sweep = new CheckoutExpirySweep(
            context,
            new CustomMediator(services.BuildServiceProvider()),
            mapping.Object,
            new Mock<IOrderEventService>().Object,
            currentUser,
            NullLogger<CheckoutExpirySweep>.Instance);

        return await sweep.RunAsync(batchSize: 50, CancellationToken.None);
    }

    private async Task<Seeded> SeedAsync(decimal total, OrderStatus orderStatus = OrderStatus.Pending)
    {
        await using var seed = _fixture.CreateContext();

        var order = new Order
        {
            OrderNumber = $"S7-{Guid.NewGuid():N}"[..12],
            Type = OrderType.DineIn,
            Status = orderStatus,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutExpirySweepTests),
        };

        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Amount = total,
            Status = PaymentStatus.Processing,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutExpirySweepTests),
        });

        seed.Orders.Add(order);

        // Saved first: Order.Id is assigned by EF at save time, not at Add.
        await seed.SaveChangesAsync();

        var session = NewSession(order.Id, decimal.ToInt64(total * 100), DateTime.UtcNow.AddMinutes(-1));
        seed.OrderCheckoutSessions.Add(session);
        await seed.SaveChangesAsync();

        return new Seeded(order.Id, session.SessionId);
    }

    private static OrderCheckoutSession NewSession(Guid orderId, long amountMinor, DateTime expiresAt) => new()
    {
        OrderId = orderId,
        SessionId = $"cs_test_{Guid.NewGuid():N}",
        Status = CheckoutSessionStatus.Created,
        Currency = "chf",
        AmountMinor = amountMinor,
        IdempotencyKey = $"checkout:{orderId}:1",
        ExpiresAt = expiresAt,
        ConnectedAccountId = ConnectedAccount,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(CheckoutExpirySweepTests),
    };

    private static StripeCheckoutSession Remote(
        string id, string status, string paymentStatus, long? amountTotalMinor) => new()
        {
            Id = id,
            Url = status == "open" ? $"https://checkout.stripe.com/c/pay/{id}" : null,
            Status = status,
            PaymentStatus = paymentStatus,
            PaymentIntentId = PaymentIntent,
            AmountTotalMinor = amountTotalMinor,
        };

    private static Mock<IStripeCheckoutClient> StripeSays(
        string status, string paymentStatus, long? amountTotalMinor)
    {
        var mock = new Mock<IStripeCheckoutClient>();
        mock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => Remote(id, status, paymentStatus, amountTotalMinor));

        return mock;
    }

    private static ICurrentUserService CurrentUser()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.GetAuditIdentifier()).Returns("System");
        return currentUser.Object;
    }

    private sealed record Seeded(Guid OrderId, string SessionId);
}
