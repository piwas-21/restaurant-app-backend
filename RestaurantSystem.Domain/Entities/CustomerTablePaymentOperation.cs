namespace RestaurantSystem.Domain.Entities;

/// <summary>Permanent native contribution decision; no credentials, contact data or order identity.</summary>
public sealed class CustomerTablePaymentOperation
{
    public Guid OperationId { get; set; }
    public required string OwnerHash { get; set; }
    public required string PayloadHash { get; set; }
    public Guid? AttemptId { get; set; }
    public bool IsStopped { get; set; }
    public DateTime CompletedAt { get; set; }
}
