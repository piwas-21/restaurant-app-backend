namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable native order acceptance or cancellation; no credentials or contact data.</summary>
public sealed class CustomerOrderOperation
{
    public Guid OperationId { get; set; }
    public required string OwnerHash { get; set; }
    public required string PayloadHash { get; set; }
    public Guid? OrderId { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CompletedAt { get; set; }
}
