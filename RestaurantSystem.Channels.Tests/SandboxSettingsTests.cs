using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class SandboxSettingsTests
{
    [Theory]
    [InlineData("https://auth.uber.com/", "https://test-api.uber.com/")]
    [InlineData("https://sandbox-login.uber.com/", "https://api.uber.com/")]
    [InlineData("http://sandbox-login.uber.com/", "https://test-api.uber.com/")]
    [InlineData("https://sandbox-login.uber.com/", "https://test-api.uber.com/other")]
    public void ConsoleCannotUseProductionOrInsecureProviderOrigins(string auth, string api)
    {
        var options = new SandboxConsoleSettings
        {
            Enabled = true,
            PublicBaseUrl = "https://sandbox.example",
            AuthBaseUrl = auth,
            ApiBaseUrl = api,
            OwnerAccessHash = new string('0', 64),
            EncryptionKey = Convert.ToBase64String(new byte[32])
        };
        Assert.False(options.IsValid());
    }
}
