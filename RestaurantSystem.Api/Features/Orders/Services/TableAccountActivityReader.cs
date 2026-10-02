using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Reads a bounded event page per visit, including cancelled member orders.</summary>
internal static class TableAccountActivityReader
{
    internal static async Task<Dictionary<Guid, TableAccountActivityPage>> ReadManyAsync(
        ApplicationDbContext context, IReadOnlyList<Guid> sessionIds, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize < 1 || pageSize >= int.MaxValue) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (sessionIds.Count == 0) return [];
        var ids = sessionIds.ToArray();
        // Window the union in PostgreSQL: limiting after client grouping would load all history.
        var rows = await context.Database.SqlQuery<TableAccountActivityRow>($"""
            WITH events AS (
                SELECT o.id AS "Id", o.service_session_id AS "SessionId", o.id AS "OrderId",
                    o.order_number AS "OrderNumber", o.order_date AS "OccurredAt",
                    'OrderPlaced' AS "Kind", o.status AS "Status"
                FROM orders o WHERE NOT o.is_deleted AND o.service_session_id = ANY ({ids})
                UNION ALL
                SELECT h.id, o.service_session_id, o.id, o.order_number, h.changed_at,
                    'StatusChanged', h.to_status
                FROM "OrderStatusHistory" h JOIN orders o ON o.id = h.order_id
                WHERE NOT o.is_deleted AND o.service_session_id = ANY ({ids})
            ), ranked AS (
                SELECT events.*, row_number() OVER (
                    PARTITION BY "SessionId" ORDER BY "OccurredAt" DESC, "Id") AS position
                FROM events
            )
            SELECT "Id", "SessionId", "OrderId", "OrderNumber", "OccurredAt", "Kind", "Status"
            FROM ranked WHERE position <= {pageSize + 1}
            ORDER BY "SessionId", "OccurredAt" DESC, "Id"
            """).ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.SessionId).ToDictionary(group => group.Key,
            group => new TableAccountActivityPage(group.Take(pageSize).Select(row => new TableAccountActivityDto
            {
                Id = row.Id,
                OrderId = row.OrderId,
                OrderNumber = row.OrderNumber,
                Kind = row.Kind,
                Status = row.Status,
                OccurredAt = row.OccurredAt
            }).ToList(), group.Count() > pageSize));
    }
}

internal sealed record TableAccountActivityPage(List<TableAccountActivityDto> Events, bool HasMore);

internal sealed class TableAccountActivityRow
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
