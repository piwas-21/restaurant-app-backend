namespace RestaurantSystem.Domain.Entities;

/// <summary>Permanent reservation-action decision; contains no customer contact or request body.</summary>
public sealed class CustomerReservationOperation
{
    public Guid OperationId { get; set; }
    public required string OwnerHash { get; set; }
    public required string PayloadHash { get; set; }
    public Guid? ReservationId { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CompletedAt { get; set; }
}
