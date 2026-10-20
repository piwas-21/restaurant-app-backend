using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One immutable native cancellation decision; no credential or customer contact.</summary>
public sealed class CustomerOrderCancellationOperation
{
    public Guid OperationId { get; set; }
    public Guid OrderId { get; set; }
    public string OwnerHash { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int ExpectedVersion { get; set; }
    public int ObservedVersion { get; set; }
    public CustomerOrderCancellationOutcome Outcome { get; set; }
    public CustomerOrderCancellationReason? Reason { get; set; }
    public DateTime CompletedAt { get; set; }
}
