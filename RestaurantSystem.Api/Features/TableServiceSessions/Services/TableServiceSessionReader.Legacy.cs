using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public sealed partial class TableServiceSessionReader
{
    private async Task<Dictionary<Guid, List<TableServiceSessionOrderState>>> ReadLegacyOrdersBySessionAsync(
        IReadOnlyList<TableServiceSession> sessions, CancellationToken cancellationToken)
    {
        var tableIds = sessions
            .Where(session => session.TableId.HasValue)
            .Select(session => session.TableId!.Value)
            .Distinct()
            .ToArray();
        var tableNumbers = sessions
            .Where(session => session.TableNumber.HasValue)
            .Select(session => session.TableNumber!.Value)
            .Distinct()
            .ToArray();
        if (tableIds.Length == 0 && tableNumbers.Length == 0)
        {
            return [];
        }

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .Where(order => (order.TableId.HasValue && tableIds.Contains(order.TableId.Value))
                || (!order.TableId.HasValue && order.TableNumber.HasValue
                    && tableNumbers.Contains(order.TableNumber.Value)))
            .SelectCloseCharges()
            .ToListAsync(cancellationToken);
        var byTableId = rows
            .Where(row => row.TableId.HasValue)
            .GroupBy(row => row.TableId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var byTableNumber = rows
            .Where(row => !row.TableId.HasValue && row.TableNumber.HasValue)
            .GroupBy(row => row.TableNumber!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        return sessions.ToDictionary(session => session.Id, session =>
        {
            var stableRows = session.TableId.HasValue
                && byTableId.TryGetValue(session.TableId.Value, out var idRows)
                ? idRows
                : [];
            var legacyRows = session.TableNumber.HasValue
                && byTableNumber.TryGetValue(session.TableNumber.Value, out var numberRows)
                ? numberRows
                : [];
            return stableRows.Concat(legacyRows)
                .Select(row => row.ToState())
                .ToList();
        });
    }
}
