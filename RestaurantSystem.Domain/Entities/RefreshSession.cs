using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One independently rotating refresh credential issued to one user agent.</summary>
public class RefreshSession : Entity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public bool IsUsableAt(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
