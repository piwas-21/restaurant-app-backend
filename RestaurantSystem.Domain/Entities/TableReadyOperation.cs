using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable idempotency receipt for a staff table-readiness transition.</summary>
public class TableReadyOperation : Entity
{
    public Guid TableId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorUserId { get; set; }
    public UserRole ActorRole { get; set; }
    public int ExpectedReadinessVersion { get; set; }
    public bool Succeeded { get; set; }
    public string? OutcomeErrorCode { get; set; }
    public TableReadinessState OutcomeState { get; set; }
    public int OutcomeReadinessVersion { get; set; }
    public DateTime RecordedAt { get; set; }

    public virtual Table Table { get; set; } = null!;
}
