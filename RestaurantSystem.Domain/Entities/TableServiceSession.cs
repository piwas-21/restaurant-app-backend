using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Durable identity for one visit at a table. Orders join this row when they are created;
/// table number alone is never used to infer membership for this resource.
/// </summary>
public class TableServiceSession : Entity
{
    /// <summary>Numeric compatibility value; null for configured labels such as T-QA.</summary>
    public int? TableNumber { get; set; }

    /// <summary>Stable configured-table identity; null is retained for legacy visits.</summary>
    public Guid? TableId { get; set; }

    /// <summary>
    /// Optional ISO-4217 code captured for this visit. Null is intentional when the tenant has
    /// not declared a currency; the service never invents a default (in particular, CHF).
    /// </summary>
    public string? Currency { get; set; }

    public TableServiceSessionStatus Status { get; set; } = TableServiceSessionStatus.Open;

    /// <summary>Optimistic-concurrency version returned on every session read.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Monotonic revision for bill-affecting account changes. It is independent from Version so
    /// service and table lifecycle changes can keep their existing concurrency contract.
    /// </summary>
    public long AccountRevision { get; set; } = 1;

    /// <summary>Immutable charge-scope model: 0 preserves legacy allocations; 1 separates tips/fees.</summary>
    public int BillingAllocationVersion { get; set; } = 1;

    /// <summary>Advances both session-write and account-content concurrency markers.</summary>
    public void RecordAccountChange()
    {
        Version++;
        AccountRevision++;
    }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public virtual Table? Table { get; set; }
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
