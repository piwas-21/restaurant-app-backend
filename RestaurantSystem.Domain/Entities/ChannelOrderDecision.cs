using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Durable manual marketplace decision; provider confirmation gates local kitchen release.</summary>
public sealed class ChannelOrderDecision : Entity
{
    public Guid OperationId { get; set; }
    public Guid OrderId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int ExpectedVersion { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime AvailableAt { get; set; }
    public string? LastCanonicalHash { get; set; }
    public string? LastReportHash { get; set; }
    public DateTime? LastObservedAt { get; set; }
    public Order Order { get; set; } = null!;
}
