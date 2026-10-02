using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Marketplace identity and immutable monetary evidence, scoped to one tenant database.</summary>
public sealed class ExternalOrderReference : Entity
{
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalStoreId { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public string ExternalDisplayId { get; set; } = string.Empty;
    public string ExternalState { get; set; } = string.Empty;
    public DateTime LastEventAt { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>The merchant-facing amount, excluding consumer marketplace fees.</summary>
    public decimal MerchantTotal { get; set; }
    /// <summary>Null means the provider did not report tax; zero means it explicitly reported zero.</summary>
    public decimal? ReportedTax { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    /// <summary>Latest provider-read evidence; separate from the immutable import fingerprint.</summary>
    public string? CanonicalHash { get; set; }
    /// <summary>Code required to call the provider-anonymized customer number.</summary>
    public string? CustomerPhoneAccessCode { get; set; }
    public string FulfillmentType { get; set; } = string.Empty;
    public bool IsSandbox { get; set; }
    public Order Order { get; set; } = null!;
}
