namespace RestaurantSystem.Domain.Entities;

/// <summary>Restaurant-local provider binding. Contact email never supplies this identity.</summary>
public sealed class CustomerSocialIdentity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public DateTime CreatedAt { get; set; }
}
