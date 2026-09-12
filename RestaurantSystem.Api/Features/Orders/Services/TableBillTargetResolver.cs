using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Keeps the legacy table-number endpoints safe after explicit sessions exist. A table-number
/// lookup may use old unassigned rounds, or exactly one explicit session, but never silently joins
/// both sets or two sessions.
/// </summary>
public sealed class TableBillTargetResolver : ITableBillTargetResolver
{
    /// <summary>Stable wording for an ambiguous table visit, including legacy unassigned orders.</summary>
    public const string AmbiguousMessage =
        "The table has more than one possible service visit; use the explicit service session id.";

    private readonly ApplicationDbContext _context;

    public TableBillTargetResolver(ApplicationDbContext context) => _context = context;

    public async Task<TableBillTarget> ResolveAsync(int tableNumber, CancellationToken cancellationToken)
    {
        var rows = await _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.TableNumber == tableNumber
                && (!TableBillAssembler.ExcludedStatuses.Contains(order.Status)
                    || (order.Status == OrderStatus.Completed && order.RemainingAmount > 0)))
            .Select(order => new { order.ServiceSessionId })
            .ToListAsync(cancellationToken);
        var sessionIds = rows.Where(row => row.ServiceSessionId.HasValue)
            .Select(row => row.ServiceSessionId!.Value)
            .Distinct()
            .ToList();
        var hasUnassigned = rows.Any(row => !row.ServiceSessionId.HasValue);
        var activeSessions = await _context.TableServiceSessions
            .AsNoTracking()
            .Where(session => session.TableNumber == tableNumber
                && session.Status == TableServiceSessionStatus.Open)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

        if (activeSessions.Count > 1)
        {
            return Ambiguous();
        }

        if (activeSessions.Count == 1)
        {
            var activeId = activeSessions[0];
            if (hasUnassigned || sessionIds.Any(id => id != activeId))
            {
                return Ambiguous();
            }
            return new TableBillTarget(activeId, false);
        }

        if (sessionIds.Count > 1 || (sessionIds.Count == 1 && hasUnassigned))
        {
            return Ambiguous();
        }

        if (sessionIds.Count == 1)
        {
            // No active session may own an open round. Keeping this as an ambiguity rather than
            // falling back to table number prevents a closed/missing membership from being billed.
            return Ambiguous("The table's open round belongs to a closed or missing service session.");
        }

        return new TableBillTarget(null, false);
    }

    private static TableBillTarget Ambiguous(string reason = AmbiguousMessage) =>
        new(null, true, reason);
}
