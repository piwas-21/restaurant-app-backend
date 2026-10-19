using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sofra.MobileService;
using Sofra.MobileService.Directory;

namespace RestaurantSystem.IntegrationTests.Features.MobileDirectory;

public sealed class MobileDirectoryHttpTests
{
    [Fact]
    public async Task PublicResponseMatchesNativeWireShapeAndNeverPublishesInternalConfiguration()
    {
        await using var factory = new DirectoryFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/v1/directory");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(["environment", "expiresAt", "publishedAt", "revision", "schemaVersion", "tenants"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("staging", root.GetProperty("environment").GetString());
        Assert.EndsWith("Z", root.GetProperty("publishedAt").GetString(), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromHours(1), root.GetProperty("expiresAt").GetDateTime() - root.GetProperty("publishedAt").GetDateTime());
        var tenant = root.GetProperty("tenants")[0];
        Assert.Equal(MobileDirectoryFixture.AlphaId, tenant.GetProperty("id").GetGuid());
        Assert.Equal(["apiOrigin", "contractVersions", "id", "imageOrigin", "name", "slug", "status", "webOrigin"],
            tenant.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("https://alpha.example", tenant.GetProperty("apiOrigin").GetString());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await health.Content.ReadAsStringAsync());
        using var unknown = await client.GetAsync("/v1/internal-registry");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task InvalidIdentityAndUnknownPrivateDirectoryFieldsPreventHostStartup()
    {
        using var invalid = DirectoryHost(new Dictionary<string, string?> { ["MobileDirectory:Tenants:0:Id"] = Guid.Empty.ToString() });
        await Assert.ThrowsAsync<OptionsValidationException>(() => invalid.StartAsync());
        using var unknown = DirectoryHost(new Dictionary<string, string?> { ["MobileDirectory:Tenants:0:DatabasePassword"] = "must-be-refused" });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.StartAsync());
        Assert.Contains("ErrorOnUnknownConfiguration", error.Message, StringComparison.Ordinal);
        Assert.Contains("DatabasePassword", error.ToString(), StringComparison.Ordinal);
    }

    // DeferredHostBuilder can mask an expected startup failure with a disposed-provider race.
    // Exercise the same registration on a real host to observe the validation error itself.
    private static IHost DirectoryHost(IReadOnlyDictionary<string, string?> overrides)
    {
        var builder = Host.CreateApplicationBuilder();
        ConfigureDirectory(builder.Configuration, overrides);
        builder.Services.AddMobileDirectory(builder.Configuration);
        return builder.Build();
    }

    private sealed class DirectoryFactory(IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<MobileServiceProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) => ConfigureDirectory(config, overrides));
    }

    private static void ConfigureDirectory(IConfigurationBuilder config, IReadOnlyDictionary<string, string?>? overrides)
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MobileDirectory:Environment"] = "staging",
            ["MobileDirectory:Revision"] = "reviewed-1",
            ["MobileDirectory:Tenants:0:Id"] = MobileDirectoryFixture.AlphaId.ToString(),
            ["MobileDirectory:Tenants:0:Slug"] = "alpha",
            ["MobileDirectory:Tenants:0:Name"] = "Alpha",
            ["MobileDirectory:Tenants:0:ApiOrigin"] = "https://alpha.example",
            ["MobileDirectory:Tenants:0:WebOrigin"] = "https://alpha.example",
            ["MobileDirectory:Tenants:0:ImageOrigin"] = "https://images.example",
            ["MobileDirectory:Tenants:0:Status"] = "active",
            ["MobileDirectory:Tenants:0:ContractVersions:0"] = "1",
            ["InternalProvisioning:DatabasePassword"] = "not-a-secret-control",
            ["InternalProvisioning:PrivateBillingReference"] = "must-not-be-public"
        });
        if (overrides is not null) config.AddInMemoryCollection(overrides);
    }
}
