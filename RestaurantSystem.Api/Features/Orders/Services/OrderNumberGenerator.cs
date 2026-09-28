using System.Globalization;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
/// The allocation is serialised per day by a Postgres advisory lock and advances a durable database
/// watermark in the caller's order transaction. An insert trigger keeps that watermark current for
/// older application versions and direct order inserts.
/// </para>
/// <para>
/// The numeric suffix is padded to at least four digits. The watermark is independent of order
/// soft-delete visibility, so deleting an order never makes its number available again.
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

        var nextSequence = await ReserveNextSequenceAsync(date, cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"{date}{nextSequence:D4}");
    }

    private async Task<long> ReserveNextSequenceAsync(string date, CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Order-number allocation requires an active transaction.");
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO order_number_sequences (day, last_sequence)
            VALUES (to_date(@day, 'YYYYMMDD'), 1)
            ON CONFLICT (day) DO UPDATE
            SET last_sequence = order_number_sequences.last_sequence + 1
            RETURNING last_sequence
            """;
        var dayParameter = command.CreateParameter();
        dayParameter.ParameterName = "day";
        dayParameter.Value = date;
        command.Parameters.Add(dayParameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            throw new DataException("Order-number sequence allocation returned no watermark.");
        }

        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Takes the day's allocation lock, blocking until any in-flight checkout for the same day has
    /// committed or rolled back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lock is <b>transaction-scoped</b>, and that is load-bearing rather than convenient. The
    /// watermark update below is committed with the order. A lock released before commit would let
    /// the next caller reserve from a watermark that does not yet include the number just handed out
    /// — exactly the race this closes. Holding to commit also means a rolled-back checkout returns
    /// its number instead of leaving a gap, and that a crashed connection releases the lock without
    /// any cleanup path.
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
    /// than recomputed from the clock so the same tenant-calendar day labels the sequence row and
    /// keys the advisory lock.
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
