using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Schema-level guarantees for <see cref="OrderCheckoutSession"/> (S0b→S4 chain, SOFRA-PAYMENTS-PLAN
/// §5 slice 2). Driven against a real <see cref="ApplicationDbContext"/> rather than through HTTP,
/// because what is under test is what POSTGRES enforces — an assertion on the C# model would pass
/// with no index in the database at all.
///
/// <para>
/// This matters more here than for a typical table. There is <b>no webhook</b> in v1 (the platform
/// cannot register one on a connected account), so settlement has two independent callers — the
/// <c>success_url</c> return trip and the polling reconciler — which can arrive simultaneously for
/// the same session. "Settle exactly once" is a database guarantee or it is nothing.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class OrderCheckoutSessionSchemaTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public OrderCheckoutSessionSchemaTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The unique index on <c>SessionId</c> is the concurrency control for the settle path. Without
    /// it, two callers that both read "not settled yet" would both write a tender and the diner
    /// would be charged once but credited twice in our ledger.
    /// </summary>
    [Fact]
    public async Task A_session_id_cannot_be_stored_twice()
    {
        Guid orderId;
        await using (var seed = _fixture.CreateContext())
        {
            var order = NewOrder();
            seed.Orders.Add(order);
            seed.OrderCheckoutSessions.Add(NewSession(order.Id, "cs_test_duplicate"));
            await seed.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var second = _fixture.CreateContext();
        second.OrderCheckoutSessions.Add(NewSession(orderId, "cs_test_duplicate"));

        var write = async () => await second.SaveChangesAsync();

        await write.Should().ThrowAsync<DbUpdateException>(
            "the database, not the handler's read-then-write, is what makes settlement idempotent");
    }

    /// <summary>
    /// The control for the test above: the constraint is on <c>SessionId</c> alone, not on the
    /// order. An order legitimately accumulates sessions — the customer abandons a redirect, it
    /// expires, and they try again — so a per-order unique constraint would break retry.
    /// </summary>
    [Fact]
    public async Task One_order_may_have_several_sessions()
    {
        await using var ctx = _fixture.CreateContext();

        var order = NewOrder();
        ctx.Orders.Add(order);
        ctx.OrderCheckoutSessions.Add(NewSession(order.Id, "cs_test_attempt_1"));
        ctx.OrderCheckoutSessions.Add(NewSession(order.Id, "cs_test_attempt_2"));
        await ctx.SaveChangesAsync();

        (await ctx.OrderCheckoutSessions.CountAsync(s => s.OrderId == order.Id))
            .Should().Be(2, "an expired redirect must be retryable");
    }

    /// <summary>
    /// Amounts are stored in the MINOR unit as a whole number, because that is the only
    /// representation Stripe and we can compare without rounding. A decimal column here would make
    /// the settle path's `amount_total == AmountMinor` assertion a floating-point question.
    /// </summary>
    [Fact]
    public async Task Amounts_round_trip_as_whole_minor_units()
    {
        Guid sessionRowId;
        await using (var seed = _fixture.CreateContext())
        {
            var order = NewOrder();
            seed.Orders.Add(order);
            var session = NewSession(order.Id, "cs_test_amounts");
            session.AmountMinor = 4_099;          // CHF 40.99
            session.AmountReceivedMinor = 4_099;
            seed.OrderCheckoutSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionRowId = session.Id;
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.OrderCheckoutSessions.AsNoTracking().SingleAsync(s => s.Id == sessionRowId);

        stored.AmountMinor.Should().Be(4_099);
        stored.AmountReceivedMinor.Should().Be(4_099);
        stored.Currency.Should().Be("chf");
        stored.Status.Should().Be(CheckoutSessionStatus.Created);
    }

    /// <summary>
    /// An order with a live session must not be deletable. The row is the only local record that
    /// money may be in flight at Stripe, and losing it strands a payment nothing can reconcile.
    ///
    /// <para>
    /// Asserted with RAW SQL, and that is not a shortcut — it is the only way to reach the
    /// constraint. <c>Order</c> is a <c>SoftDeleteEntity</c>, so <c>Orders.Remove(...)</c> is
    /// rewritten into an UPDATE and the FK never fires; that version of this test passes whatever
    /// the delete behaviour is, including <c>Cascade</c>. Loading the session first doesn't help
    /// either — EF's change tracker then severs the relationship client-side and throws before any
    /// SQL is sent. A real <c>DELETE</c> is what the Restrict rule exists for: the purge paths that
    /// bypass the filter (GDPR erasure) and anything operating on the database directly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_order_carrying_a_session_cannot_be_hard_deleted()
    {
        Guid orderId;
        await using (var seed = _fixture.CreateContext())
        {
            var order = NewOrder();
            seed.Orders.Add(order);
            seed.OrderCheckoutSessions.Add(NewSession(order.Id, "cs_test_restrict"));
            await seed.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var ctx = _fixture.CreateContext();

        var hardDelete = async () => await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM orders WHERE id = {0}", orderId);

        await hardDelete.Should().ThrowAsync<Exception>(
            "the FK is Restrict, so Postgres refuses to strand a session that may have money in flight");
    }

    private static Order NewOrder() => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"CS-{Guid.NewGuid().ToString()[..8]}",
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(OrderCheckoutSessionSchemaTests),
    };

    private static OrderCheckoutSession NewSession(Guid orderId, string sessionId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        SessionId = sessionId,
        Currency = "chf",
        AmountMinor = 1_000,
        IdempotencyKey = $"checkout:{orderId}:1",
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        ConnectedAccountId = "acct_test_schema",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(OrderCheckoutSessionSchemaTests),
    };
}
