using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable receipt for a confirmed physical-table recovery operation.</summary>
public sealed class TableOccupancyRecoveryOperation : Entity
{
    public Guid TableId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public UserRole ActorRole { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string PreviewFingerprint { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int ExpectedReadinessVersion { get; set; }
    public int OutcomeReadinessVersion { get; set; }
    public TableReadinessState OutcomeReadinessState { get; set; }
    public int? ExpectedSessionVersion { get; set; }
    public long? ExpectedAccountRevision { get; set; }
    public int? OutcomeSessionVersion { get; set; }
    public long? OutcomeAccountRevision { get; set; }
    public DateTime? VisitReleasedAt { get; set; }
    public DateTime RecordedAt { get; set; }
}
