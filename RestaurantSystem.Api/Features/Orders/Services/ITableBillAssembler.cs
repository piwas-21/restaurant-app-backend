using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Assembles ONE bill for a dine-in table: the union of that table's open orders
/// (every ordering round the guests made), oldest first, with per-order grouping
/// preserved and bill-level sums on top.
/// </summary>
public interface ITableBillAssembler
{
    /// <summary>
    /// Builds the bill for <paramref name="tableNumber"/>. Returns <c>null</c> when the table
    /// has no open orders — the caller decides how an empty bill reads (404 vs empty state).
    /// </summary>
    Task<TableBillDto?> AssembleAsync(int tableNumber, CancellationToken cancellationToken);

    /// <summary>Builds a bill from immutable service-session membership.</summary>
    Task<TableBillDto?> AssembleAsync(Guid serviceSessionId, CancellationToken cancellationToken);
}
