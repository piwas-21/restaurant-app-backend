using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S4 (SOFRA-PAYMENTS-PLAN §5 slice 4). Driven against a real database with Stripe faked, because
/// the properties under test are about what we PERSIST and what we ask Stripe to charge — neither
/// of which a test against a fake DbContext would establish.
///
/// <para>
/// The headline property is the direct continuation of S0b: <b>the charge is the persisted
/// <c>order.Total</c></b>. The command carries an order id and nothing else, so there is no amount
/// for a caller to declare — these tests are what stop a future edit adding one back.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class CreateCheckoutSessionCommandHandlerTests : IAsyncLifetime
{
    private const string ConnectedAccount = "acct_test_connected";

    private readonly DatabaseFixture _fixture;

    public CreateCheckoutSessionCommandHandlerTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The point of the slice. 42.50 on the order becomes 4250 minor units at Stripe and 4250 on
    /// the row S5 will assert against — one number, from the column the server wrote.
    /// </summary>
    [Fact]
    public async Task The_charge_is_the_persisted_order_total()
    {
        var orderId = await SeedOrderAsync(total: 42.50m);
        var checkout = FakeCheckout(out var captured);

        var result = await HandleAsync(orderId, checkout);

        captured.Should().ContainSingle();
        captured[0].AmountMinor.Should().Be(4250);
        captured[0].Currency.Should().Be("chf");
        result.Data!.AmountMinor.Should().Be(4250);

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.SingleAsync(s => s.OrderId == orderId);
        row.AmountMinor.Should().Be(4250);
        row.Currency.Should().Be("chf");
        row.Status.Should().Be(CheckoutSessionStatus.Created);
        row.ConnectedAccountId.Should().Be(ConnectedAccount);
        // Nothing about the payment itself is known yet — those belong to S5's settle path, and a
        // value here would be a claim about money that has not moved.
        row.PaymentIntentId.Should().BeNull();
        row.AmountReceivedMinor.Should().BeNull();
        row.OrderPaymentId.Should().BeNull();
    }

    /// <summary>
    /// Just over Stripe's documented 30-minute minimum, rather than the 24 h default: the
    /// reconciler (S7) cancels an order on expiry, and a full day of an abandoned order sitting
    /// un-cancelled is not a useful state for the kitchen. The extra minute absorbs the round trip
    /// — Stripe evaluates the minimum against ITS clock, after the request arrives.
    /// </summary>
    [Fact]
    public async Task A_session_expires_just_past_the_stripe_minimum()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);

        var before = DateTime.UtcNow;
        var result = await HandleAsync(orderId, checkout);

        result.Data!.ExpiresAt.Should().BeCloseTo(before.AddMinutes(31), TimeSpan.FromSeconds(30));
        captured[0].ExpiresAt.Should().BeCloseTo(before.AddMinutes(31), TimeSpan.FromSeconds(30));
        // The margin is the point: an exact 30:00 stamped before the round trip arrives at Stripe
        // as 29:59 and the whole session is rejected.
        captured[0].ExpiresAt.Should().BeAfter(before.AddMinutes(30));
    }

    /// <summary>
    /// The key is <c>checkout:{orderId}:{attempt}</c>, and the attempt is DERIVED from how many
    /// sessions the order already has. That is what makes two concurrent callers compute the same
    /// key, so Stripe replays one session rather than minting two payable ones.
    /// </summary>
    [Fact]
    public async Task The_idempotency_key_names_the_order_and_the_attempt()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);

        await HandleAsync(orderId, checkout);

        captured[0].IdempotencyKey.Should().Be($"checkout:{orderId}:1");
    }

    /// <summary>
    /// A double-click, or a diner hitting back and retrying, must not leave two payable sessions
    /// against one order — they could pay both.
    /// </summary>
    [Fact]
    public async Task A_second_call_reuses_the_live_session()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);

        var first = await HandleAsync(orderId, checkout);
        var second = await HandleAsync(orderId, checkout);

        second.Data!.SessionId.Should().Be(first.Data!.SessionId);
        captured.Should().ContainSingle("the second call must not mint a second session");

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.CountAsync(s => s.OrderId == orderId)).Should().Be(1);
    }

    /// <summary>
    /// Our <c>Created</c> is only a claim about the past — there is no webhook to correct it (plan
    /// §4). When Stripe says the session is gone, the row is marked and a fresh session is minted,
    /// rather than handing the diner a dead page.
    /// </summary>
    [Fact]
    public async Task An_expired_session_is_recorded_and_replaced()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);

        var first = await HandleAsync(orderId, checkout);

        // Stripe now reports it expired. This is exactly the closed-tab case S7 also sweeps.
        checkout.Setup(c => c.GetAsync(first.Data!.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutSession
            {
                Id = first.Data!.SessionId,
                Status = "expired",
                PaymentStatus = "unpaid",
                Url = null,
            });

        var second = await HandleAsync(orderId, checkout);

        second.Data!.SessionId.Should().NotBe(first.Data!.SessionId);
        captured.Should().HaveCount(2);
        // Attempt 2, because the dead session still counts — replaying key 1 would return the
        // expired session Stripe just refused.
        captured[1].IdempotencyKey.Should().Be($"checkout:{orderId}:2");

        await using var verify = _fixture.CreateContext();
        var dead = await verify.OrderCheckoutSessions.SingleAsync(s => s.SessionId == first.Data!.SessionId);
        dead.Status.Should().Be(CheckoutSessionStatus.Expired);
        dead.LastError.Should().Contain("expired");
    }

    /// <summary>
    /// If Stripe says Checkout was completed, S4 refuses and changes nothing. Settling belongs to
    /// S5 — half-settling here would put a second writer on the one transition that must happen
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// The <c>complete</c>/<c>unpaid</c> row is the one that matters. A delayed-notification method
    /// — SEPA, Klarna, Sofort, all reachable because <c>PaymentMethodTypes</c> is deliberately left
    /// unset so Stripe picks dynamically — completes with <c>payment_status</c> still
    /// <c>unpaid</c> while funds clear. Keyed off "paid" alone, the handler would read that as
    /// unpaid, expire a session the diner has already been through, mint a second one for the same
    /// amount, and the diner would pay twice.
    /// </remarks>
    [Theory]
    [InlineData("complete", "paid")]
    [InlineData("complete", "unpaid")]
    public async Task A_completed_session_is_refused_and_left_for_the_settle_path(
        string status, string paymentStatus)
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);
        var first = await HandleAsync(orderId, checkout);

        checkout.Setup(c => c.GetAsync(first.Data!.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutSession
            {
                Id = first.Data!.SessionId,
                Status = status,
                PaymentStatus = paymentStatus,
                PaymentIntentId = "pi_test_1",
                AmountTotalMinor = 1000,
            });

        var act = async () => await HandleAsync(orderId, checkout);

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("*already in progress*");
        captured.Should().ContainSingle("a completed session must never be replaced with a second one");

        await using var verify = _fixture.CreateContext();
        var row = await verify.OrderCheckoutSessions.SingleAsync(s => s.SessionId == first.Data!.SessionId);
        row.Status.Should().Be(CheckoutSessionStatus.Created, "S5 owns that transition, not S4");
        row.OrderPaymentId.Should().BeNull();
    }

    /// <summary>
    /// A <c>Created</c> row whose session Stripe no longer recognises — a key or connected account
    /// swapped underneath us, a database restored across environments. The row is retired and a
    /// fresh session minted, because the alternative is an order nobody can ever pay.
    /// </summary>
    [Fact]
    public async Task A_session_stripe_does_not_recognise_is_retired_not_fatal()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);
        var first = await HandleAsync(orderId, checkout);

        checkout.Setup(c => c.GetAsync(first.Data!.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);

        var second = await HandleAsync(orderId, checkout);

        second.Data!.SessionId.Should().NotBe(first.Data!.SessionId);
        captured.Should().HaveCount(2);

        await using var verify = _fixture.CreateContext();
        var dead = await verify.OrderCheckoutSessions.SingleAsync(s => s.SessionId == first.Data!.SessionId);
        dead.Status.Should().Be(CheckoutSessionStatus.Expired);
        dead.LastError.Should().Contain("unknown");
    }

    /// <summary>
    /// Stripe answering with no hosted-page URL. The refusal is the easy half; the half worth
    /// pinning is that the session is still RECORDED, so the attempt counter advances. Without the
    /// row, the next call replays idempotency key 1, Stripe hands back the same unusable session,
    /// and the order can never be paid.
    /// </summary>
    [Fact]
    public async Task A_session_with_no_url_is_recorded_as_failed_so_the_next_attempt_is_fresh()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured, urlless: true);

        var act = async () => await HandleAsync(orderId, checkout);
        await act.Should().ThrowAsync<BadRequestException>();

        await using (var verify = _fixture.CreateContext())
        {
            var row = await verify.OrderCheckoutSessions.SingleAsync(s => s.OrderId == orderId);
            row.Status.Should().Be(CheckoutSessionStatus.Failed);
            row.LastError.Should().NotBeNullOrWhiteSpace();
        }

        // The next attempt must NOT replay key 1 — that is what would wedge the order.
        var recovered = FakeCheckout(out var second);
        await HandleAsync(orderId, recovered);

        captured[0].IdempotencyKey.Should().Be($"checkout:{orderId}:1");
        second[0].IdempotencyKey.Should().Be($"checkout:{orderId}:2");
    }

    [Fact]
    public async Task A_closed_order_is_refused()
    {
        var orderId = await SeedOrderAsync(total: 10m, status: OrderStatus.Cancelled);
        var checkout = FakeCheckout(out var captured);

        var act = async () => await HandleAsync(orderId, checkout);

        await act.Should().ThrowAsync<BadRequestException>();
        captured.Should().BeEmpty("a closed order must never reach Stripe");
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        var act = async () => await HandleAsync(Guid.NewGuid(), FakeCheckout(out _));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// The fleet's actual state: every tenant today has the module off and no Stripe account. The
    /// module gate on the controller answers "did they buy it"; this answers "can they transact",
    /// and a bought module with no connected account is a real state — it is where a tenant sits
    /// between signup and finishing Stripe's hosted onboarding.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_tenant_is_refused_before_anything_is_written()
    {
        var orderId = await SeedOrderAsync(total: 10m);
        var checkout = FakeCheckout(out var captured);

        var gateway = new Mock<IStripeGateway>();
        gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var act = async () => await HandleAsync(orderId, checkout, gateway);

        await act.Should().ThrowAsync<BadRequestException>();
        captured.Should().BeEmpty();

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AnyAsync()).Should().BeFalse();
    }

    private async Task<Guid> SeedOrderAsync(decimal total, OrderStatus status = OrderStatus.Pending)
    {
        await using var seed = _fixture.CreateContext();
        var order = new Order
        {
            OrderNumber = $"S4-{Guid.NewGuid():N}"[..12],
            Type = OrderType.Takeaway,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CreateCheckoutSessionCommandHandlerTests),
        };

        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>
    /// A Stripe stand-in that records what it was asked to charge. Recording the REQUEST rather
    /// than asserting on a response is deliberate: what matters is the number that leaves this
    /// codebase, not the one Stripe echoes back.
    /// </summary>
    private static Mock<IStripeCheckoutClient> FakeCheckout(
        out List<CheckoutSessionRequest> captured, bool urlless = false)
    {
        var requests = new List<CheckoutSessionRequest>();
        captured = requests;

        // Per-INSTANCE, because a test that builds a second fake is standing in for a second
        // Stripe call, and Stripe never mints one session id twice. Without this the ids collide
        // on the unique index and the test fails for a reason the product does not have.
        var instance = Guid.NewGuid().ToString("N")[..6];

        var mock = new Mock<IStripeCheckoutClient>();
        mock.Setup(c => c.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSessionRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                var id = $"cs_test_{instance}_{requests.Count}_{request.OrderId:N}";
                return new StripeCheckoutSession
                {
                    Id = id,
                    Url = urlless ? null : $"https://checkout.stripe.com/c/pay/{id}",
                    Status = "open",
                    PaymentStatus = "unpaid",
                    AmountTotalMinor = request.AmountMinor,
                };
            });

        // Default: whatever was minted is still open. Individual tests override per session id.
        mock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new StripeCheckoutSession
            {
                Id = id,
                Url = $"https://checkout.stripe.com/c/pay/{id}",
                Status = "open",
                PaymentStatus = "unpaid",
            });

        return mock;
    }

    private async Task<RestaurantSystem.Api.Common.Models.ApiResponse<
        RestaurantSystem.Api.Features.Payments.Dtos.CheckoutSessionDto>> HandleAsync(
        Guid orderId,
        Mock<IStripeCheckoutClient> checkout,
        Mock<IStripeGateway>? gateway = null)
    {
        await using var ctx = _fixture.CreateContext();

        gateway ??= ConfiguredGateway();

        var currentUser = new Mock<ICurrentUserService>();
        // Default-interface methods aren't invoked by Moq; stub explicitly. "System" is the real
        // answer here — guest checkout has no account to name (ADR-004).
        currentUser.Setup(u => u.GetAuditIdentifier()).Returns("System");

        var handler = new CreateCheckoutSessionCommandHandler(
            ctx,
            gateway.Object,
            checkout.Object,
            // The REAL reuse service, not a stub. It is what stands between a diner and paying
            // twice, so faking it here would delete the property most of these tests exist for.
            new CheckoutSessionReuse(ctx, checkout.Object),
            currentUser.Object,
            Options.Create(new LocalizationSettings { Currency = "CHF" }),
            NullLogger<CreateCheckoutSessionCommandHandler>.Instance);

        return await handler.Handle(new CreateCheckoutSessionCommand { OrderId = orderId }, CancellationToken.None);
    }

    private static Mock<IStripeGateway> ConfiguredGateway()
    {
        var gateway = new Mock<IStripeGateway>();
        gateway.SetupGet(g => g.IsConfigured).Returns(true);
        gateway.SetupGet(g => g.ConnectedAccountId).Returns(ConnectedAccount);
        return gateway;
    }
}
