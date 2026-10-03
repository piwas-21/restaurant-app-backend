using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

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
    /// <summary>Typed owner for idempotent replay; null on legacy plans whose owner cannot be proven.</summary>
    public Guid? ActorId { get; set; }
    /// <summary>Typed owner category; null on legacy plans whose owner cannot be proven.</summary>
    public AccountPaymentActorKind? ActorKind { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    public Guid? SupersedesPlanId { get; set; }
    public TableServiceSession? ServiceSession { get; set; }
}
