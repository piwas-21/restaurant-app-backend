namespace RestaurantSystem.Domain.Entities;

/// <summary>One immutable provider request per native order; no credentials or customer contact.</summary>
public sealed class CustomerOrderCheckoutJournal
{
    public Guid OrderId { get; set; }
    public Guid OperationId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public long? ApplicationFeeMinor { get; set; }
    public string ProviderAccountId { get; set; } = string.Empty;
    public bool ProviderLiveMode { get; set; }
    public string CreateIdempotencyKey { get; set; } = string.Empty;
    public string CreatePayloadHash { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime MaximumCreateRetryAt { get; set; }
    public string? ProviderSessionId { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
}
