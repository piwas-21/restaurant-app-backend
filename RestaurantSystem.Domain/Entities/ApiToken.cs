using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A named, scoped, expiring credential for a MACHINE client — an agent or a script that would
/// otherwise have to hold a human admin's password (docs/plans/API-TOKENS-PLAN.md).
/// </summary>
/// <remarks>
/// The plaintext token is never stored: only <see cref="TokenHash"/> (SHA-256, the lookup key)
/// and <see cref="Prefix"/> (display only). A lost token is revoked and replaced, never recovered.
/// </remarks>
public class ApiToken : Entity
{
    /// <summary>Human label chosen by the admin, e.g. "menu seeder".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Base64 SHA-256 of the plaintext. Uniquely indexed: it is what authentication looks up,
    /// so the index is load-bearing for latency, not only for integrity.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// First 12 characters of the plaintext (e.g. <c>sk_live_a1b2</c>) so an admin can tell two
    /// tokens apart in a list. Short enough to be useless to an attacker who reads the table.
    /// </summary>
    public required string Prefix { get; set; }

    /// <summary>Granted scopes — the <c>ApiTokenScopes</c> vocabulary.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Hard expiry, UTC. Never null: an agent credential must outlive nothing.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When an admin revoked it, UTC. Null = live.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Last successful authentication, UTC, written at most once a minute. Enough to answer
    /// "is anything still using this" without an UPDATE on every request.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Whether this token authenticates at <paramref name="utcNow"/>.</summary>
    public bool IsUsableAt(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
