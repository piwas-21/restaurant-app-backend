using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Frozen provider request and bounded recovery evidence for one account contribution.</summary>
public sealed class AccountCheckoutJournal : Entity
{
    public Guid AttemptId { get; set; }
    public int StartedAttemptVersion { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ProviderAccountId { get; set; } = string.Empty;
    public bool ProviderLiveMode { get; set; }
    public string CreateIdempotencyKey { get; set; } = string.Empty;
    public string CreatePayloadHash { get; set; } = string.Empty;
    public string ReturnBaseUrl { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime MaximumCreateRetryAt { get; set; }
    public string? ProviderSessionId { get; set; }
    public string? ProviderIntentId { get; set; }
    public string? ProviderChargeId { get; set; }
    public long ProviderCapturedMinor { get; set; }
    public long ProviderRefundedMinor { get; set; }
    public bool ReconciliationRequired { get; set; }
    public DateTime? CancelRequestedAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime NextReconcileAt { get; set; }
    public bool WebhookWakeupPending { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public int ReconcileFailureCount { get; set; }
    public string? LastFailureCode { get; set; }
    public string? ReceiptCredentialHash { get; set; }
    public DateTime? ReceiptExpiresAt { get; set; }
    public AccountPaymentAttempt? Attempt { get; set; }
}
