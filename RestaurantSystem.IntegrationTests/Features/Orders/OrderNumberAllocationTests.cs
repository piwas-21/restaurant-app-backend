using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins <see cref="OrderNumberGenerator"/> against the concurrent-checkout race.
/// </summary>
/// <remarks>
/// <para>
/// The allocator derives the next daily sequence from the highest committed order number, so two
/// checkouts that read before either commits compute the same successor and the second INSERT
/// violates the unique index on <c>order_number</c>. <c>CreateOrderCommandHandler</c> rethrows,
/// nothing maps <c>DbUpdateException</c>, and the guest gets an unhandled 500 at checkout.
/// </para>
/// <para>
/// The interleaving cannot be forced by statement order alone, and an earlier version of this test
/// that tried to — allocate on A, start B, commit A — <em>passed against the unguarded allocator</em>:
/// B's SELECT is asynchronous, it lost the race to A's commit, and so it read the committed row and
/// answered correctly. What the test asserts instead is the property that actually distinguishes the
/// two: with the guard in place B <b>cannot answer at all</b> until A commits.
/// </para>
/// <para>
/// "Cannot answer" is established by observing the wait itself from a third connection —
/// <c>pg_locks</c> showing an ungranted advisory lock keyed on today — rather than by sleeping and
/// finding B unfinished. That distinguishes blocked from merely slow, costs milliseconds instead of
/// a fixed delay, and pins the lock to the <em>day</em>, so a regression to a constant key (which
/// would serialise every day onto one lock, and still pass a timing-based test) fails here. B is
/// also awaited the moment it completes, so a lock statement that throws surfaces its own exception
/// instead of being reported as "B answered early" — the opposite diagnosis.
/// </para>
/// <para>
/// Two independent <see cref="ApplicationDbContext"/> instances drive the race, plus a third to
/// observe it: the race is between connections, and a single context would serialise it away and
/// pass against the bug.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class OrderNumberAllocationTests : IntegrationTestBase
{
    /// <summary>Upper bound on how long the second allocation is watched for. Only paid on failure.</summary>
    private static readonly TimeSpan BlockedProbeTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// A clock on the UTC zone, so <see cref="Today"/> below still names the day this allocator
    /// picks and the cases about the LOCK keep asserting the lock. The day the prefix is built from
    /// is the tenant's since backend #372 and is pinned separately, at the bottom of this file.
    /// </summary>
    private static readonly ITenantClock UtcClock = new FixedTenantClock("UTC");

    public OrderNumberAllocationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Concurrent_checkouts_are_allocated_distinct_order_numbers()
    {
        await using var contextA = DatabaseFixture.CreateContext();
        await using var contextB = DatabaseFixture.CreateContext();

        await using var transactionA = await contextA.Database.BeginTransactionAsync();
        var numberA = await new OrderNumberGenerator(contextA, UtcClock).GenerateAsync();
        AddOrder(contextA, numberA);
        await contextA.SaveChangesAsync();

        // B starts while A's row exists but is still uncommitted, so nothing B can read tells it
        // that numberA is taken.
        await using var transactionB = await contextB.Database.BeginTransactionAsync();
        var allocateB = new OrderNumberGenerator(contextB, UtcClock).GenerateAsync();

        var blocked = await WaitUntilBlockedOnTodaysLockAsync(allocateB);
        blocked.Should().BeTrue(
            "an allocation that can answer while another transaction's number is uncommitted has "
            + "already computed a colliding number, whatever it goes on to return");

        await transactionA.CommitAsync();

        var numberB = await allocateB;
        numberB.Should().NotBe(numberA, "the second checkout must not reuse a committed order number");

        AddOrder(contextB, numberB);
        var saveB = async () => await contextB.SaveChangesAsync();
        await saveB.Should().NotThrowAsync(
            "a duplicate order number violates IX_orders_order_number and reaches the guest as a 500");
        await transactionB.CommitAsync();
    }

    /// <summary>
    /// The guard is transaction-scoped, so outside a transaction it would be taken and released by
    /// the same statement and mutual exclusion would silently be nil. That failure mode is
    /// invisible — allocation keeps working, and only concurrent load reveals it — so an allocator
    /// asked to run without a transaction refuses instead of pretending to be safe.
    /// </summary>
    [Fact]
    public async Task Allocating_outside_a_transaction_is_refused()
    {
        await using var context = DatabaseFixture.CreateContext();
        context.Database.CurrentTransaction.Should().BeNull();

        var allocate = async () => await new OrderNumberGenerator(context, UtcClock).GenerateAsync();

        await allocate.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// The number is printed on kitchen tickets, so the human-facing shape is part of the contract:
    /// <c>yyyyMMdd</c> followed by a 4-digit daily sequence that starts at 0001 and increments by
    /// one. The guard must not change any of that.
    /// </summary>
    [Fact]
    public async Task Sequential_allocations_keep_the_daily_format_and_increment_by_one()
    {
        await using var context = DatabaseFixture.CreateContext();
        var generator = new OrderNumberGenerator(context, UtcClock);

        var allocated = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var number = await generator.GenerateAsync();
            AddOrder(context, number);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            allocated.Add(number);
        }

        allocated.Should().Equal($"{Today}0001", $"{Today}0002", $"{Today}0003");
    }

    private static string Today => DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Waits until <paramref name="allocation"/> is parked on today's allocation lock. Returns false
    /// if it answers instead — which is the bug — and rethrows if it faulted.
    /// </summary>
    private async Task<bool> WaitUntilBlockedOnTodaysLockAsync(Task<string> allocation)
    {
        await using var observer = DatabaseFixture.CreateContext();
        var dayKey = int.Parse(Today, CultureInfo.InvariantCulture);
        var deadline = DateTime.UtcNow + BlockedProbeTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (allocation.IsCompleted)
            {
                // Awaited rather than inspected: a lock statement that threw must surface its own
                // exception here, not be reported as the allocator having answered early.
                await allocation;
                return false;
            }

            if (await WaitersOnDayLockAsync(observer, dayKey) > 0)
            {
                return true;
            }

            await Task.Delay(PollInterval);
        }

        return false;
    }

    /// <summary>
    /// Counts backends queued on the advisory lock for <paramref name="dayKey"/>, which is
    /// <c>objid</c> — so this asserts the lock is keyed on the day, not merely that some lock exists.
    /// </summary>
    /// <remarks>
    /// <c>objsubid = 2</c> selects the two-integer key space and <b>1</b> would be the single-bigint
    /// one. That is the way round Postgres actually reports it, which is worth stating because it
    /// reads backwards: this was first written as <c>= 1</c> and the probe then found nothing while
    /// the lock was demonstrably held, since a <c>bigint</c> lock is the 64-bit "sub-object" and the
    /// key pair the second form.
    /// </remarks>
    private static Task<int> WaitersOnDayLockAsync(ApplicationDbContext observer, int dayKey) =>
        observer.Database
            .SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND objsubid = 2 AND objid::bigint = {dayKey}")
            .SingleAsync();

    private static void AddOrder(ApplicationDbContext context, string orderNumber)
    {
        var now = DateTime.UtcNow;
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 0m,
            Total = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderNumberAllocationTests),
        });
    }

    /// <summary>
    /// The <c>yyyyMMdd</c> prefix is the day a HUMAN reads off a kitchen ticket, so it is the
    /// restaurant's day: on UTC the number rolled over at 02:00 local in Zurich summer (backend
    /// #372, the cosmetic sibling of the Z-report defect — uniqueness and the day lock are keyed on
    /// this same string either way, so only the printed day was ever wrong).
    /// <para>
    /// Anchored to the REAL instant, like the Z-report's default-day test: a literal date cannot
    /// tell the tenant's day apart from UTC's on a CI runner, so the zone is chosen from the current
    /// UTC hour to guarantee they differ — and that premise is asserted, not assumed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_daily_prefix_is_the_tenant_day_not_the_UTC_day()
    {
        var utcNow = DateTimeOffset.UtcNow;

        // POSIX sign convention: Etc/GMT+12 is UTC-12, Etc/GMT-12 is UTC+12. Both ids are present
        // in mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0.
        var clock = new FixedTenantClock(utcNow.Hour < 12 ? "Etc/GMT+12" : "Etc/GMT-12");
        var tenantDay = clock.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        tenantDay.Should().NotBe(Today, "otherwise a UTC prefix would satisfy this test too");

        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var number = await new OrderNumberGenerator(context, clock).GenerateAsync();

        number.Should().StartWith(tenantDay);
        await transaction.RollbackAsync();
    }
}
