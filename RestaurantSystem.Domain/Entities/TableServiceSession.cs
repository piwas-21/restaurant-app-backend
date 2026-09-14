using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Durable identity for one visit at a table. Orders join this row when they are created;
/// table number alone is never used to infer membership for this resource.
/// </summary>
public class TableServiceSession : Entity
{
    public int TableNumber { get; set; }

    /// <summary>
    /// Optional ISO-4217 code captured for this visit. Null is intentional when the tenant has
    /// not declared a currency; the service never invents a default (in particular, CHF).
    /// </summary>
    public string? Currency { get; set; }

    public TableServiceSessionStatus Status { get; set; } = TableServiceSessionStatus.Open;

    /// <summary>Optimistic-concurrency version returned on every session read.</summary>
    public int Version { get; set; } = 1;

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
