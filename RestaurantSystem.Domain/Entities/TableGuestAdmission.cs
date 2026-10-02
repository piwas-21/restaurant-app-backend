using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One rotatable, visit-scoped code that authorizes guest participant joins.</summary>
public sealed class TableGuestAdmission : Entity
{
    public Guid ServiceSessionId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public TableServiceSession ServiceSession { get; set; } = null!;
    public ICollection<TableGuestParticipant> Participants { get; set; } = new List<TableGuestParticipant>();
}
