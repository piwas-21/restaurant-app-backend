using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

/// <summary>Limits the order list to a named read surface.</summary>
public enum OrderListScope
{
    /// <summary>Return all orders that match the remaining filters.</summary>
    All = 0,

    /// <summary>Return unfinished orders and collectible completed orders.</summary>
    Operational = 1,
}

/// <summary>Query orders with optional filters and legacy page pagination.</summary>
/// <remarks>
/// <para>
/// <c>page</c>/<c>pageSize</c> remain the legacy offset contract. For the operational scope, clients
/// may instead use <c>syncCursor</c>: omit it to begin a Snapshot walk, then pass
/// <c>sync.nextCursor</c> until the terminal <c>sync.watermark</c>. Passing that watermark begins a
/// Changes walk. The cursor is server-protected and binds all filters, the caller and tenant.
/// </para>
/// <para>
/// Operational snapshots ignore calendar date bounds, as they did before synchronization was added.
/// Date bounds remain UTC instants for the All scope. <c>tenantDay</c> selects one venue-calendar
/// day; <c>tenantStartDay</c>/<c>tenantEndDay</c> select an inclusive venue-calendar range whose UTC
/// boundaries are computed by the server.
/// </para>
/// </remarks>
public record GetOrdersQuery(
    string? Status,
    string? PaymentStatus,
    string? OrderType,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? UserId,
    string? Search,
    bool? IsFocusOrder,
    DateOnly? TenantDay = null,
    DateOnly? TenantStartDay = null,
    DateOnly? TenantEndDay = null,
    DateTime? ModifiedSince = null,
    string? OrderBy = "OrderDate",
    bool Descending = true,
    int Page = 1,
    int PageSize = 10,
    OrderListScope Scope = OrderListScope.All,
    int? TableNumber = null,
    string? SyncCursor = null
) : IQuery<ApiResponse<PagedResult<OrderDto>>>;
