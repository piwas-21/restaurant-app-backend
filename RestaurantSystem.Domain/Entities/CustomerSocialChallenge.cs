namespace RestaurantSystem.Domain.Entities;

/// <summary>Short-lived one-use native login/link challenge; no provider token or contact data.</summary>
public sealed class CustomerSocialChallenge
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Provider { get; set; }
    public required string Flow { get; set; }
    public required string ProofHash { get; set; }
    public required string Nonce { get; set; }
    public Guid? UserId { get; set; }
    public string? SecurityStampHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
