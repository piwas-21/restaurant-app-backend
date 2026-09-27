using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable, idempotent request snapshot for a large option-set fan-out.</summary>
public class OptionSetMaterializationJob : Entity
{
    public Guid OptionSetId { get; set; }
    public int SetVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string RequestJson { get; set; } = "{}";
    public string Status { get; set; } = "queued";
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public virtual ICollection<OptionSetMaterializationJobTarget> Targets { get; set; } = [];
}
