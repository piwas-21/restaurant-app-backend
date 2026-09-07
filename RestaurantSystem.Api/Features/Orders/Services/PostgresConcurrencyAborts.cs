using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// The one definition of "Postgres killed this statement because another writer won": a
/// serialization failure (40001, SSI), a deadlock (40P01), or a statement that hit the
/// already-aborted transaction state (25P02). The bill-payment command maps all three to its
/// "review the bill" refusal — raw, or EF-wrapped in a <see cref="DbUpdateException"/> raised
/// by a SaveChanges inside the transaction — and the fidelity-award best-effort catch must NOT
/// swallow them (a swallowed abort dooms the ambient transaction; its COMMIT then reads as
/// success over an empty ledger).
/// </summary>
public static class PostgresConcurrencyAborts
{
    public const string SerializationFailure = "40001";
    public const string Deadlock = "40P01";
    public const string AbortedTransaction = "25P02";

    /// <summary>True when <paramref name="ex"/> is one of those aborts. The concrete
    /// <c>SqlState</c> is handed back for the log.</summary>
    public static bool IsMatch(Exception ex, out string sqlState)
    {
        var pg = ex switch
        {
            PostgresException direct => direct,
            DbUpdateException { InnerException: PostgresException wrapped } => wrapped,
            _ => null,
        };

        sqlState = pg?.SqlState ?? "";
        return pg is not null
            && (sqlState is SerializationFailure or Deadlock or AbortedTransaction);
    }
}
