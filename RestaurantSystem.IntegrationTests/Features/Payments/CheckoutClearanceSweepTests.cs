using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S7's second sweep — the one that closes the exposure S5 shipped knowingly (plan §6c).
/// </summary>
/// <remarks>
/// A Checkout session is <c>complete</c> the moment the diner finishes, whether or not the money
/// cleared, so a delayed-notification method (SEPA, Klarna, Sofort) is booked as a captured tender
/// while funds are still in flight. The settle command returns early on any non-<c>Created</c>
/// session, so before this sweep nothing ever re-read Stripe for those rows: if the payment later
/// failed, <b>there was no mechanism that could discover it</b>.
/// <para>
/// The asymmetry these tests pin is the whole design: a failed payment corrects the MONEY and
/// deliberately leaves the ORDER alone, because by the time a delayed method fails the food may have
/// been served, and cancelling a real service record to tidy an accounting one is the worse error.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class CheckoutClearanceSweepTests : IAsyncLifetime
{
    private const string ConnectedAccount = "acct_test_connected";
    private const string PaymentIntent = "pi_test_clearing";

    private readonly DatabaseFixture _fixture;

    public CheckoutClearanceSweepTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_cleared_payment_is_marked_and_the_tender_is_untouched()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(IntentIs("succeeded"));

        report.Cleared.Should().Be(1);
        report.Reversed.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        var order = await verify.Orders.AsNoTracking().SingleAsync();

        session.ReconciledAt.Should().NotBeNull("the row must stop being re-read once Stripe is definite");
        session.LastError.Should().BeNull();
        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Completed);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    /// <summary>
    /// The headline. Money is corrected; the order is not. Both halves are asserted, because
    /// getting either one wrong is a different real-world failure — a false ledger, or a cancelled
    /// order somebody already ate.
    /// </summary>
    [Theory]
    [InlineData("canceled")]
    [InlineData("requires_payment_method")]
    public async Task A_payment_that_fails_after_capture_reverses_the_money_but_not_the_order(string status)
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(IntentIs(status));

        report.Reversed.Should().Be(1);

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking()
            .Include(o => o.StatusHistory)
            .SingleAsync();

        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Failed);
        order.TotalPaid.Should().Be(0m, "Failed is not IsCaptured(), so the money leaves every total");
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);

        order.Status.Should().Be(OrderStatus.Confirmed, "the food may already have been served");
        order.StatusHistory.Should().NotContain(h => h.ToStatus == OrderStatus.Cancelled);

        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        session.ReconciledAt.Should().NotBeNull();
        session.LastError.Should().Contain(status);
    }

    /// <summary>
    /// Still clearing. The marker means "Stripe is definite", not "we looked once" — leaving it null
    /// is what makes the next sweep ask again, and is the difference between a sweep that resolves a
    /// delayed payment and one that silently gives up on it.
    /// </summary>
    [Theory]
    [InlineData("processing")]
    [InlineData("requires_action")]
    public async Task A_payment_still_in_flight_is_left_for_the_next_sweep(string status)
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(IntentIs(status));

        report.Cleared.Should().Be(0);
        report.Reversed.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        session.ReconciledAt.Should().BeNull();
        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Completed);
    }

    /// <summary>
    /// An unknown status must NOT be read as a failure. Stripe adds PaymentIntent states over time,
    /// and acting on one this code has never seen would un-book money on a guess.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_status_is_treated_as_still_in_flight()
    {
        await SeedAsync(total: 42.50m);

        var report = await RunAsync(IntentIs("some_status_stripe_added_later"));

        report.Reversed.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Completed);
        (await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync()).ReconciledAt.Should().BeNull();
    }

    /// <summary>
    /// Stripe cannot see the intent — a live/test key swap, a restored database. Nothing further is
    /// knowable, so the row is marked to stop re-reading it, and the money is left for a human.
    /// </summary>
    [Fact]
    public async Task An_intent_stripe_does_not_recognise_is_marked_and_left_for_a_human()
    {
        await SeedAsync(total: 42.50m);

        var stripe = new Mock<IStripeCheckoutClient>();
        stripe.Setup(c => c.GetPaymentIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripePaymentIntent?)null);

        var report = await RunAsync(stripe);

        report.Cleared.Should().Be(0);
        report.Reversed.Should().Be(0);

        await using var verify = _fixture.CreateContext();
        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();

        session.ReconciledAt.Should().NotBeNull();
        session.LastError.Should().NotBeNullOrWhiteSpace();
        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status
            .Should().Be(PaymentStatus.Completed, "un-booking money on an unreadable intent would be a guess");
    }

    /// <summary>
    /// A delayed debit can bounce days after staff already refunded it. Reversing then would flip
    /// the tender out of the captured sum while its <c>RefundedAmount</c> keeps being subtracted —
    /// <c>TotalPaid</c> goes NEGATIVE, which is exactly the double-count
    /// <c>PaymentStatusExtensions</c>' own remarks exist to prevent. Two things went wrong and only
    /// a human can say which money is real.
    /// </summary>
    [Fact]
    public async Task A_refunded_tender_is_never_reversed_automatically()
    {
        await SeedAsync(total: 42.50m, refunded: true);

        var report = await RunAsync(IntentIs("canceled"));

        report.Reversed.Should().Be(0);
        report.NeedsAttention.Should().Be(1);

        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking().SingleAsync();

        (await verify.OrderPayments.AsNoTracking().SingleAsync()).Status
            .Should().Be(PaymentStatus.Refunded, "the tender is left exactly as the refund left it");
        order.TotalPaid.Should().BeGreaterThanOrEqualTo(0m, "money must never go negative");

        var session = await verify.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        session.ReconciledAt.Should().NotBeNull("re-reading it every sweep helps nobody");
        session.LastError.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The "order untouched" claim, driven from a status other than the seed's usual Confirmed —
    /// otherwise it would pass trivially for any status the sweep happened to write.
    /// </summary>
    [Fact]
    public async Task A_pending_order_stays_pending_when_its_payment_fails()
    {
        await SeedAsync(total: 42.50m, orderStatus: OrderStatus.Pending);

        await RunAsync(IntentIs("canceled"));

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task A_second_pass_finds_nothing_to_do()
    {
        await SeedAsync(total: 42.50m);
        var stripe = IntentIs("succeeded");

        await RunAsync(stripe);
        var second = await RunAsync(stripe);

        second.Examined.Should().Be(0, "a reconciled row leaves the query");
        second.Cleared.Should().Be(0);
    }

    private async Task<CheckoutClearanceSweepReport> RunAsync(Mock<IStripeCheckoutClient> stripe)
    {
        await using var context = _fixture.CreateContext();
        var currentUser = CurrentUser();

        var sweep = new CheckoutClearanceSweep(
            context,
            stripe.Object,
            // The REAL payment builder: TotalPaid and PaymentStatus are assertions in this file.
            new OrderPaymentBuilder(currentUser),
            currentUser,
            NullLogger<CheckoutClearanceSweep>.Instance);

        return await sweep.RunAsync(batchSize: 50, CancellationToken.None);
    }

    /// <summary>
    /// The state S5 leaves behind for a delayed-notification payment: session Completed, tender
    /// Completed and captured, dine-in order already auto-confirmed. Exactly the row that, before
    /// this sweep, nothing would ever look at again.
    /// </summary>
    private async Task SeedAsync(
        decimal total, bool refunded = false, OrderStatus orderStatus = OrderStatus.Confirmed)
    {
        await using var seed = _fixture.CreateContext();

        var order = new Order
        {
            OrderNumber = $"S7c-{Guid.NewGuid():N}"[..12],
            Type = OrderType.DineIn,
            Status = orderStatus,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = total,
            Total = total,
            TotalPaid = total,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutClearanceSweepTests),
        };

        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Amount = total,
            Status = refunded ? PaymentStatus.Refunded : PaymentStatus.Completed,
            IsRefunded = refunded,
            RefundedAmount = refunded ? total : null,
            TransactionId = PaymentIntent,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutClearanceSweepTests),
        });

        seed.Orders.Add(order);
        await seed.SaveChangesAsync();

        seed.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = $"cs_test_{Guid.NewGuid():N}",
            Status = CheckoutSessionStatus.Completed,
            PaymentIntentId = PaymentIntent,
            Currency = "chf",
            AmountMinor = decimal.ToInt64(total * 100),
            AmountReceivedMinor = decimal.ToInt64(total * 100),
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            ConnectedAccountId = ConnectedAccount,
            OrderPaymentId = order.Payments.First().Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutClearanceSweepTests),
        });

        await seed.SaveChangesAsync();
    }

    private static Mock<IStripeCheckoutClient> IntentIs(string status)
    {
        var mock = new Mock<IStripeCheckoutClient>();
        mock.Setup(c => c.GetPaymentIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new StripePaymentIntent { Id = id, Status = status });

        return mock;
    }

    private static ICurrentUserService CurrentUser()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.GetAuditIdentifier()).Returns("System");
        return currentUser.Object;
    }
}
