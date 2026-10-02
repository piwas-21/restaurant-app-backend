namespace RestaurantSystem.Api.Settings;

/// <summary>Server-only route from this tenant API to its exact approved channel gateway binding.</summary>
public sealed class DeliveryChannelManagementSettings
{
    public const string SectionName = "DeliveryChannelManagement";
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string GatewayBaseUrl { get; set; } = string.Empty;
    public string ServerCredential { get; set; } = string.Empty;
    public int HttpTimeoutSeconds { get; set; } = 20;

    public bool IsValid()
    {
        if (!Enabled) return true;
        return TenantId.Length is > 0 and <= 100 && !TenantId.Any(char.IsControl)
            && Uri.TryCreate(GatewayBaseUrl, UriKind.Absolute, out var gateway)
            && gateway.Scheme == Uri.UriSchemeHttps && gateway.Port == 443 && gateway.AbsolutePath == "/"
            && gateway.UserInfo.Length == 0 && gateway.Query.Length == 0 && gateway.Fragment.Length == 0
            && ServerCredential.Length is >= 32 and <= 256 && !ServerCredential.Any(char.IsControl)
            && HttpTimeoutSeconds is >= 5 and <= 30;
    }
}
