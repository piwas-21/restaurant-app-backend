namespace RestaurantSystem.Domain.Entities;

/// <summary>Append-only charge evidence; accepted food and payment records remain unchanged.</summary>
public sealed record OrderBillCharge(Guid OperationId, string Key, string Label, decimal Amount,
    string Currency, DateTime CreatedAt, string CreatedBy,
    decimal? EnteredValue = null, string Effect = "Increase", string Calculation = "Fixed",
    long? BasisMinor = null, IReadOnlyList<BillDiscountAllocation>? Allocations = null);

/// <summary>Frozen reduction of an accepted food unit; amounts are nonnegative minor units.</summary>
public sealed record BillDiscountAllocation(Guid OrderItemId, int StartOrdinal, int Count, long MinorPerUnit);
