using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Loads table/session rows with PostgreSQL locks for lifecycle serialization.</summary>
public static class TableServiceSessionRowLock
{
    public static Task<Table?> LoadTableAsync(
        ApplicationDbContext context, Guid tableId, CancellationToken cancellationToken) =>
        context.Tables
            // Table.Id is the referenced key for order inserts. PostgreSQL's FK check takes
            // KEY SHARE; NO KEY UPDATE keeps readiness/lifecycle writes serialized while allowing
            // a staff round holding the session lock to insert its table-linked order.
            .FromSqlInterpolated($"SELECT * FROM \"Tables\" WHERE id = {tableId} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<Table?> LoadTableByNumberAsync(
        ApplicationDbContext context, int tableNumber, CancellationToken cancellationToken)
    {
        var label = tableNumber.ToString(CultureInfo.InvariantCulture);
        return context.Tables
            .FromSqlInterpolated($"SELECT * FROM \"Tables\" WHERE table_number = {label} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static Task<TableServiceSession?> LoadAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken) =>
        context.TableServiceSessions
            .FromSqlInterpolated($"SELECT * FROM table_service_sessions WHERE id = {serviceSessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Locks lifecycle rows in the common Table-then-Session order used by guest admission.
    /// The initial identity read is only a locator; it is revalidated after both locks are held.
    /// </summary>
    public static async Task<TableServiceSessionLifecycleRows> LoadForLifecycleAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var located = await context.TableServiceSessions.AsNoTracking()
            .Where(session => session.Id == serviceSessionId)
            .Select(session => new { session.TableId, session.TableNumber })
            .SingleOrDefaultAsync(cancellationToken);
        var table = located?.TableId is Guid tableId
            ? await LoadTableAsync(context, tableId, cancellationToken)
            : located?.TableNumber is int tableNumber
                ? await LoadTableByNumberAsync(context, tableNumber, cancellationToken)
                : null;
        var session = await LoadAsync(context, serviceSessionId, cancellationToken);
        var identityChanged = session is not null && (located is null
            || session.TableId != located.TableId
            || session.TableNumber != located.TableNumber
            || !located.TableId.HasValue && (!located.TableNumber.HasValue
                || table is null
                || table.TableNumber != located.TableNumber.Value.ToString(CultureInfo.InvariantCulture)));
        return new TableServiceSessionLifecycleRows(table, session, identityChanged);
    }
}

public sealed record TableServiceSessionLifecycleRows(
    Table? Table,
    TableServiceSession? Session,
    bool IdentityChanged);
