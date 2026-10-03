using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Idempotency record tying one guest operation to one visit order.</summary>
public sealed class TableGuestRoundOperation : Entity
{
    public Guid ServiceSessionId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid OperationId { get; set; }
    public Guid OrderId { get; set; }
    public string RequestHash { get; set; } = string.Empty;

    public TableServiceSession ServiceSession { get; set; } = null!;
    public TableGuestParticipant Participant { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
