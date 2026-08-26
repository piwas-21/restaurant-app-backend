namespace RestaurantSystem.Api.Features.ApiTokens.Dtos;

/// <summary>
/// A machine token as an admin sees it in the list. Carries NO plaintext — the plaintext exists
/// only in the create response (docs/plans/API-TOKENS-PLAN.md §4).
/// </summary>
public record ApiTokenDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Display-only first characters of the plaintext, e.g. <c>sk_live_a1b2</c>.</summary>
    public required string Prefix { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// <c>active</c> | <c>expired</c> | <c>revoked</c>. Derived server-side so the UI and the
    /// authentication handler cannot disagree about what "usable" means.
    /// </summary>
    public required string Status { get; init; }
}
