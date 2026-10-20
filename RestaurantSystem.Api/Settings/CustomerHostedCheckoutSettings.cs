using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Settings;

/// <summary>Reviewed central-link configuration; absent settings keep native creation unavailable.</summary>
public sealed class CustomerHostedCheckoutSettings
{
    public const string SectionName = "CustomerHostedCheckout";
    public const int MaximumReconciliationBudgetSeconds = 30;
    public bool Enabled { get; set; }
    public int ReconciliationBudgetSeconds { get; set; } = 5;
    public string[] HostedPageOrigins { get; set; } = [];
    public string LinkOrigin { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public bool IsValid() => !Enabled || ReconciliationBudgetSeconds is >= 1 and <= MaximumReconciliationBudgetSeconds
        && NativeCheckoutRouting.IsValidConfiguration(LinkOrigin, TenantSlug)
        && HostedPageOrigins is { Length: > 0 and <= 4 } && HostedPageOrigins.Distinct(StringComparer.Ordinal).Count() == HostedPageOrigins.Length
        && HostedPageOrigins.All(origin => NativeCheckoutRouting.IsValidConfiguration(origin, TenantSlug));
}
