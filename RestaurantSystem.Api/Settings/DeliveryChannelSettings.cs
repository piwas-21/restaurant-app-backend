namespace RestaurantSystem.Api.Settings;

/// <summary>Deployment-owned tenant/store binding. Never supplied by the incoming order.</summary>
public sealed class DeliveryChannelSettings
{
    public const string SectionName = "DeliveryChannels";
    public bool Enabled { get; set; }
    public bool SandboxOnly { get; set; } = true;
    public List<DeliveryChannelStore> Stores { get; set; } = [];
}

public sealed class DeliveryChannelStore
{
    public string Provider { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IsSandbox { get; set; }
}
