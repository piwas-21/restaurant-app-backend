using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Opaque credential for one guest on exactly one table-service visit.</summary>
public sealed class TableGuestParticipant : Entity
{
    public Guid ServiceSessionId { get; set; }
    public Guid AdmissionId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public TableServiceSession ServiceSession { get; set; } = null!;
    public TableGuestAdmission Admission { get; set; } = null!;
}
