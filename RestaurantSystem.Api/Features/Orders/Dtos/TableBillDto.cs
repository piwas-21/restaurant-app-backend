namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>
/// One bill for a dine-in table. Legacy table-number reads contain non-terminal rounds; an
/// explicit service-session read uses immutable membership and also retains settled rounds for a
/// durable receipt. Per-order grouping is preserved so the waiter can see each round.
/// </summary>
public record TableBillDto
{
    public int TableNumber { get; set; }

    /// <summary>Explicit visit identity. Null on the legacy table-number bill.</summary>
    public Guid? ServiceSessionId { get; set; }

    /// <summary>Optimistic-concurrency version for an explicit visit.</summary>
    public int? ServiceSessionVersion { get; set; }

    /// <summary>Currency captured for the visit; null means no currency is declared.</summary>
    public string? Currency { get; set; }

    /// <summary>True only when the legacy table-number lookup found incompatible visits.</summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>Server clock instant the bill was assembled (UTC).</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Bill rounds, oldest first.</summary>
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
