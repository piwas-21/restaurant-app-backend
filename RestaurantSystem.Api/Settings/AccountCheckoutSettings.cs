using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Additive table-contribution return paths and bounded recovery policy.</summary>
public sealed class AccountCheckoutSettings
{
    public const string SectionName = "AccountCheckout";
    public string ReturnPath { get; set; } = "/table-account";
    [Range(30, 1440)]
    public int CheckoutLifetimeMinutes { get; set; } = 60;
    [Range(1, 23)]
    public int MaximumCreateRetryHours { get; set; } = 12;
    [Range(5, 3600)]
    public int ReconciliationIntervalSeconds { get; set; } = 30;
    [Range(1, 100)]
    public int ReconciliationBatchSize { get; set; } = 25;
    [Range(1, 720)]
    public int ReceiptLifetimeHours { get; set; } = 72;
}
