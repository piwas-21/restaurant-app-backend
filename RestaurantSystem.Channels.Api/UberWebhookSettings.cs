namespace RestaurantSystem.Channels.Api;

public sealed class UberWebhookSettings
{
    public const string Section = "UberSandbox";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public Guid[] StoreIds { get; set; } = [];
    public int MaxBodyBytes { get; set; } = 65_536;
    public int RequestsPerMinute { get; set; } = 120;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && StoreIds.Length > 0 && StoreIds.All(id => id != Guid.Empty);
}
