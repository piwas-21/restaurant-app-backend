using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Allocates the human-facing daily order number (<c>yyyyMMdd</c> + a 4-digit sequence).
/// </summary>
/// <remarks>
/// <para>
/// Originally extracted verbatim from <c>CreateOrderCommandHandler</c>. The derivation below —
/// read the day's highest number, add one — is a read-then-increment, and on its own it is unsafe
/// under concurrency: two checkouts that read before either commits compute the same successor, and
/// the second INSERT violates the unique index on <c>order_number</c>. The caller rethrows, nothing
/// maps <c>DbUpdateException</c>, and the guest gets an unhandled 500 at checkout. It is not
/// theoretical — it failed a frontend e2e run on 2026-08-09 and passed on a re-run, because
/// Playwright drives specs in parallel.
/// </para>
/// <para>
/// The allocation is therefore serialised per day by a Postgres advisory lock. The number stays
/// derived from the orders table rather than from a counter, which keeps it consistent with every
/// row the day already has — including any inserted by hand — and needs no schema of its own.
/// </para>
/// <para>
/// Two bounds are inherited rather than introduced, both far outside this tenant's volume and
/// neither made worse here. The lexical <c>OrderByDescending</c> below agrees with numeric order
/// only while the sequence is fixed-width, so at 10 000 orders in one day <c>D4</c> emits five
/// digits, <c>"…10000"</c> sorts below <c>"…9999"</c>, and allocation collides permanently. And the
/// read runs through the soft-delete filter while the unique index does not, so a soft-deleted
/// order's number would be invisible here yet still occupy the index — unreachable today, because
/// <c>DeleteOrderCommand</c> hard-deletes and nothing sets <c>IsDeleted</c> on an order.
/// </para>
/// </remarks>
public class OrderNumberGenerator : IOrderNumberGenerator
{
    /// <summary>
    /// First element of the advisory lock key, reserved for daily order-number allocation; the day
    /// itself is the second. This is the only advisory lock in the application — a later one must
    /// pick a different value here.
    /// </summary>
    /// <remarks>
    /// Nothing else contends for it today, and that was checked rather than assumed: EF's Postgres
    /// provider takes no advisory lock at all for migrations, it issues
    /// <c>LOCK TABLE … IN ACCESS EXCLUSIVE MODE</c>. Note also that the two-integer key used here is
    /// a separate space from the single-<c>bigint</c> one, so a library using that form cannot
    /// collide with this whatever value it picks — but another user of the two-integer form could,
    /// which is what reserving this constant is for. In <c>pg_locks</c> the two appear as
    /// <c>objsubid</c> 2 and 1 respectively, in that order — this lock is the <c>objsubid = 2</c> one.
    /// </remarks>
    private const int OrderNumberLockNamespace = 1;

    private readonly ApplicationDbContext _context;
    private readonly ITenantClock _clock;

    public OrderNumberGenerator(ApplicationDbContext context, ITenantClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        // The day a human reads off the number, so it is the tenant's day (backend #372) — on UTC
        // the number rolled over at 02:00 local. Uniqueness and the advisory lock below are
        // unaffected either way: both key on this same string.
        var date = _clock.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        await LockDayAsync(date, cancellationToken);

        var lastOrder = await _context.Orders
            .Where(o => o.OrderNumber.StartsWith(date))  // EF translates to SQL LIKE; no StringComparison overload is translatable
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = 1;
        if (lastOrder is not null)
        {
            var lastSequence = lastOrder.OrderNumber[8..];
            if (int.TryParse(lastSequence, out var seq))
            {
                sequence = seq + 1;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{date}{sequence:D4}");
    }

    /// <summary>
    /// Takes the day's allocation lock, blocking until any in-flight checkout for the same day has
    /// committed or rolled back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lock is <b>transaction-scoped</b>, and that is load-bearing rather than convenient. The
    /// read below only ever sees committed rows, so a lock released before commit would let the next
    /// caller read a table that does not yet contain the number just handed out — exactly the race
    /// this closes. Holding to commit also means a rolled-back checkout returns its number instead of
    /// leaving a gap, and that a crashed connection releases the lock without any cleanup path.
    /// </para>
    /// <para>
    /// It follows that there must be a transaction to scope it to. Without one the statement below
    /// takes the lock and releases it in the same breath, leaving allocation working perfectly and
    /// wholly unguarded — a gate that fails open, and one only concurrent load would ever expose. So
    /// a caller that has not opened a transaction is refused rather than quietly served.
    /// </para>
    /// <para>
    /// This also assumes READ COMMITTED, the Postgres default and what
    /// <c>BeginTransactionAsync()</c> asks for. Under REPEATABLE READ the waiting caller would resume
    /// on a snapshot taken before it queued and read a stale maximum, so the lock would serialise the
    /// callers without fixing the number they compute.
    /// </para>
    /// </remarks>
    /// <param name="date">
    /// The <c>yyyyMMdd</c> prefix the number will carry. The lock key is parsed from this rather
    /// than recomputed from the clock so the two cannot drift apart: were "today" ever to become
    /// restaurant-local rather than UTC, a key derived independently could end up guarding a
    /// different day than the number it protects, and the guard would silently stop guarding.
    /// </param>
    private async Task LockDayAsync(string date, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(OrderNumberGenerator)} must be called inside a transaction. Its allocation "
                + "lock is transaction-scoped, so without one the order number is derived unguarded "
                + "and two concurrent checkouts can collide on the unique order_number index.");
        }

        var dayKey = int.Parse(date, CultureInfo.InvariantCulture);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({OrderNumberLockNamespace}, {dayKey})", cancellationToken);
    }
}
