namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>
/// ONE bill for a dine-in table: the union of that table's still-open orders
/// (guests order in several rounds; the till must settle them together).
/// An order is open while its <c>Status</c> is neither Completed nor Cancelled —
/// the same set <see cref="Commands.CompleteAllTableOrdersCommand"/> closes when the
/// table is cleared. Per-order grouping is preserved so the waiter can see which
/// round each line belongs to; the sums are what the guest actually owes.
/// </summary>
public record TableBillDto
{
    public int TableNumber { get; set; }

    /// <summary>Server clock instant the bill was assembled (UTC).</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Open orders for the table, oldest round first.</summary>
    public List<OrderDto> Orders { get; set; } = new();

    public int OrderCount { get; set; }

    // Bill-level sums over <see cref="Orders"/>.
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Discount { get; set; }
    public decimal Tip { get; set; }
    public decimal Total { get; set; }

    /// <summary>Every captured tender across the bill, minus what was refunded.</summary>
    public decimal TotalPaid { get; set; }

    /// <summary>
    /// What the table still owes: the per-order outstanding amounts, each clamped at
    /// zero, summed. Clamping keeps an overpaid order (a tender that exceeded one
    /// order's total) from offsetting what a sibling order still owes — overpayment
    /// stays on its own order; a new bill tender never reaches back into it.
    /// </summary>
    public decimal Remaining { get; set; }
}
