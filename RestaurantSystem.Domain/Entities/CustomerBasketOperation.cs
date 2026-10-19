namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable receipt for one customer basket add. No credentials or request body are stored.</summary>
public sealed class CustomerBasketOperation
{
    public Guid OperationId { get; set; }
    public required string OwnerHash { get; set; }
    public required string PayloadHash { get; set; }
    public Guid BasketId { get; set; }
    public DateTime AppliedAt { get; set; }
}
