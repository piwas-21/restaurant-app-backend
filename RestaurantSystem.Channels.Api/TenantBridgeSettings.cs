namespace RestaurantSystem.Channels.Api;

public sealed class TenantBridgeSettings
{
    public const string Section = "TenantBridge";
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public DateTimeOffset EnrollmentStartedAt { get; set; }
    public int PollSeconds { get; set; } = 5;
    public int RetrySeconds { get; set; } = 30;
    public int PayloadRetentionDays { get; set; } = 7;
    public TenantStoreBinding Store { get; set; } = new();

    public bool IsValid()
    {
        if (PollSeconds is < 1 or > 60 || RetrySeconds is < 10 or > 300 || PayloadRetentionDays is < 1 or > 7) return false;
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
