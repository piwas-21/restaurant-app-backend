namespace RestaurantSystem.Channels.Api;

public sealed class TenantBridgeSettings
{
    public const string Section = "TenantBridge";
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public bool DispatchDecisions { get; set; }
    public bool SyncAvailability { get; set; }
    public bool UseTenantCatalogue { get; set; }
    public int AvailabilityPollSeconds { get; set; } = 30;
    public int AvailabilityMaxWrites { get; set; } = 10;
    public bool RecoverCreatedOrders { get; set; }
    public int RecoveryPollSeconds { get; set; } = 30;
    public int RecoveryListLimit { get; set; } = 200;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public DateTimeOffset EnrollmentStartedAt { get; set; }
    public int PollSeconds { get; set; } = 5;
    public int RetrySeconds { get; set; } = 30;
    public int PayloadRetentionDays { get; set; } = 7;
    public TenantStoreBinding Store { get; set; } = new();

    private bool HasOperationalBounds() => HttpTimeoutSeconds is >= 5 and <= 30
        && (!DispatchDecisions || Enabled) && (!RecoverCreatedOrders || Enabled)
        && (!SyncAvailability || Enabled && Store.CatalogueApiToken.Length > 0)
        && (!UseTenantCatalogue || Enabled && SyncAvailability && Store.CatalogueApiToken.Length > 0)
        && AvailabilityPollSeconds is >= 10 and <= 300 && AvailabilityMaxWrites is >= 1 and <= 20
        && RecoveryPollSeconds is >= 5 and <= 300 && RecoveryListLimit is >= 1 and <= 200 && PollSeconds is >= 1 and <= 60
        && RetrySeconds is >= 10 and <= 300 && PayloadRetentionDays is >= 1 and <= 7;

    public bool IsValid()
    {
        if (!HasOperationalBounds()) return false;
        if (!Enabled) return true;
        return Store.StoreId != Guid.Empty && Store.TenantId.Length is > 0 and <= 100
            && Store.ApiToken.Length > 0 && Store.Currency is "EUR" or "CHF"
            && EnrollmentStartedAt != default && EnrollmentStartedAt <= DateTimeOffset.UtcNow
            && Uri.TryCreate(Store.BaseUrl, UriKind.Absolute, out var origin)
            && origin.Scheme == Uri.UriSchemeHttps && origin.Port == 443 && origin.AbsolutePath == "/"
            && origin.UserInfo.Length == 0 && origin.Query.Length == 0 && origin.Fragment.Length == 0
            && Store.CatalogueRevision.Length is > 0 and <= 128 && Store.PublishedMenuHash.Length == 64
            && Store.PublishedMenuHash.All(Uri.IsHexDigit) && Store.Items.Count is > 0 and <= 200
            && Store.Items.All(item => item.ProviderItemId.Length is > 0 and <= 128 && item.ProductId != Guid.Empty)
            && Store.Items.Select(item => item.ProviderItemId).Distinct(StringComparer.Ordinal).Count() == Store.Items.Count;
    }
}
