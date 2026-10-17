namespace RestaurantSystem.Domain.Entities;

/// <summary>Append-only charge evidence; accepted food and payment records remain unchanged.</summary>
public sealed record OrderBillCharge(Guid OperationId, string Key, string Label, decimal Amount,
    string Currency, DateTime CreatedAt, string CreatedBy);
