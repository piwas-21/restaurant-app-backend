using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable idempotency and actor audit row for the authenticated counter-order API.</summary>
public class StaffOrderOperation : Entity
{
    public Guid OperationId { get; set; }
    public Guid OrderId { get; set; }
    public StaffOrderOperationKind Kind { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int? ExpectedVersion { get; set; }

    public virtual Order Order { get; set; } = null!;
}
