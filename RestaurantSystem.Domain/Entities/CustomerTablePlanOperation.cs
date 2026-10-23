namespace RestaurantSystem.Domain.Entities;

/// <summary>Permanent original native split decision, independent of participant/account erasure.</summary>
public sealed class CustomerTablePlanOperation
{
    public Guid OperationId { get; set; }
    public required string OwnerHash { get; set; }
    public required string PayloadHash { get; set; }
    public Guid? PlanId { get; set; }
    public bool IsStopped { get; set; }
    public DateTime CompletedAt { get; set; }
}
