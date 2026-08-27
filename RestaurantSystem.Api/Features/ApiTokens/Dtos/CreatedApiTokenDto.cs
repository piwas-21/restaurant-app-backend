namespace RestaurantSystem.Api.Features.ApiTokens.Dtos;

/// <summary>
/// The create-token response — the ONE place a plaintext token is ever returned
/// (docs/plans/API-TOKENS-PLAN.md §4). There is no endpoint that can show it again.
/// </summary>
public record CreatedApiTokenDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>The plaintext. Shown once; only its SHA-256 is stored.</summary>
    public required string Token { get; init; }

    public required string Prefix { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required DateTime CreatedAt { get; init; }
}
