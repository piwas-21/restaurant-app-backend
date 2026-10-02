using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantBridgeSettingsTests
{
    [Fact]
    public void DisabledDefaultsAreCompatibleButCleanupCadenceStillMustBeBounded()
    {
        Assert.True(new TenantBridgeSettings().IsValid());
        Assert.False(new TenantBridgeSettings { PollSeconds = 0 }.IsValid());
        Assert.False(new TenantBridgeSettings { PollSeconds = -1 }.IsValid());
        Assert.False(new TenantBridgeSettings { PayloadRetentionDays = 8 }.IsValid());
    }

    private static TenantBridgeSettings Configured() => new()
    {
        Enabled = true,
        EnrollmentStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        Store = new()
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = "test-tenant",
            BaseUrl = "https://tenant.example/",
            ApiToken = "public-fixture-only",
            Currency = "EUR",
            CatalogueRevision = "published-v1",
            PublishedMenuHash = new string('a', 64),
            Items = [new() { ProviderItemId = "published-meal-v1", ProductId = Guid.NewGuid() }]
        },
    };

    [Theory]
    [InlineData("origin")]
    [InlineData("origin-path")]
    [InlineData("credentials-in-url")]
    [InlineData("unknown-currency")]
    [InlineData("missing-enrollment")]
    [InlineData("future-enrollment")]
    [InlineData("missing-key")]
    [InlineData("ambiguous-map")]
    [InlineData("invalid-menu-hash")]
    public void EnabledBridgeRejectsUnreviewableConfiguration(string mutation)
    {
        var options = Configured(); Assert.True(options.IsValid());
        switch (mutation)
        {
            case "origin": options.Store.BaseUrl = "http://tenant.example/"; break;
            case "origin-path": options.Store.BaseUrl = "https://tenant.example/other"; break;
            case "credentials-in-url": options.Store.BaseUrl = new UriBuilder("https://tenant.example/") { UserName = "public-fixture-user" }.Uri.AbsoluteUri; break;
            case "unknown-currency": options.Store.Currency = "USD"; break;
            case "missing-enrollment": options.EnrollmentStartedAt = default; break;
            case "future-enrollment": options.EnrollmentStartedAt = DateTimeOffset.UtcNow.AddDays(1); break;
            case "missing-key": options.Store.ApiToken = ""; break;
            case "ambiguous-map": options.Store.Items.Add(options.Store.Items[0]); break;
            case "invalid-menu-hash": options.Store.PublishedMenuHash = new string('z', 64); break;
        }
        Assert.False(options.IsValid());
    }
}
