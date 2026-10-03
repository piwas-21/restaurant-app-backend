using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>A reviewed fixed debt scope; later rounds never enlarge its shares implicitly.</summary>
public sealed class AccountEqualSharePlan : Entity
{
    public Guid ServiceSessionId { get; set; }
    public Guid OperationId { get; set; }
    public long AccountRevision { get; set; }
    public long TotalMinor { get; set; }
    public int ShareCount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ScopeJson { get; set; } = string.Empty;
    public DateTime? InvalidatedAt { get; set; }
    public Guid? SupersedesPlanId { get; set; }
    public TableServiceSession? ServiceSession { get; set; }
}
