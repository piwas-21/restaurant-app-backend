using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable outcome and immutable input for one target in a fan-out job.</summary>
public class OptionSetMaterializationJobTarget : Entity
{
    public Guid JobId { get; set; }
    public int Sequence { get; set; }
    public string TargetKey { get; set; } = string.Empty;
    public Guid TargetProductId { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public virtual OptionSetMaterializationJob Job { get; set; } = null!;
}
