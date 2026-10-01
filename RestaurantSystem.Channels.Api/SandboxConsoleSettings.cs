namespace RestaurantSystem.Channels.Api;

public sealed class SandboxConsoleSettings
{
    public const string Section = "SandboxConsole";
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string AuthBaseUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string OwnerAccessHash { get; set; } = string.Empty;
    public string EncryptionKey { get; set; } = string.Empty;
    public int SessionMinutes { get; set; } = 60;
    public int AuthorizationMinutes { get; set; } = 10;
    public int MaxResponseBytes { get; set; } = 1_048_576;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public const string CookieName = "__Host-sofra-channel-session";
    public const string CallbackPath = "/api/sandbox/uber/callback";

    public bool IsValid()
    {
        if (!Enabled) return true;
        return IsSecureOrigin(PublicBaseUrl)
            && IsSecureOrigin(AuthBaseUrl, "sandbox-login.uber.com")
            && IsSecureOrigin(ApiBaseUrl, "test-api.uber.com")
            && OwnerAccessHash.Length == 64 && OwnerAccessHash.All(Uri.IsHexDigit)
            && Convert.TryFromBase64String(EncryptionKey, new byte[32], out var count) && count == 32
            && SessionMinutes is >= 5 and <= 120 && AuthorizationMinutes is >= 1 and <= 15
            && MaxResponseBytes is >= 65_536 and <= 2_097_152 && HttpTimeoutSeconds is >= 1 and <= 30;
    }

    private static bool IsSecureOrigin(string value, string? host = null)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Port == 443 && uri.AbsolutePath == "/" && uri.Query.Length == 0
            && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0
            && (host is null || string.Equals(uri.Host, host, StringComparison.Ordinal));
}
