namespace RestaurantSystem.Channels.Api;

public sealed class TenantManagementGatewaySettings
{
    public const string Section = "TenantManagement";
    public const string CallbackPath = SandboxConsoleSettings.CallbackPath;
    public const string ReturnPath = "/admin/delivery-channels/callback";

    public bool Enabled { get; set; }
    public bool CategorySelectionEnabled { get; set; }
    public string CredentialHash { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public int AuthorizationMinutes { get; set; } = 10;

    public bool IsValid()
    {
        if (!Enabled) return !CategorySelectionEnabled;
        return CredentialHash.Length == 64 && CredentialHash.All(Uri.IsHexDigit)
            && SecureCallback(CallbackUrl) && SecureReturn(ReturnUrl)
            && AuthorizationMinutes is >= 1 and <= 15;
    }

    private static bool SecureCallback(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Port == 443 && uri.AbsolutePath == CallbackPath && uri.Query.Length == 0
            && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;

    private static bool SecureReturn(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Port == 443 && uri.AbsolutePath == ReturnPath && uri.Query.Length == 0
            && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;
}
