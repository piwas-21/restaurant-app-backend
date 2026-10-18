using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sofra.MobileService;

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
        using var unknown = await client.GetAsync("/v1/internal-registry");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public void InvalidIdentityAndUnknownPrivateDirectoryFieldsPreventHostStartup()
    {
        using var invalid = new DirectoryFactory(new Dictionary<string, string?> { ["MobileDirectory:Tenants:0:Id"] = Guid.Empty.ToString() });
        Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
        using var unknown = new DirectoryFactory(new Dictionary<string, string?> { ["MobileDirectory:Tenants:0:DatabasePassword"] = "must-be-refused" });
        var error = Assert.Throws<InvalidOperationException>(() => unknown.CreateClient());
        Assert.Contains("ErrorOnUnknownConfiguration", error.Message, StringComparison.Ordinal);
    }

    private sealed class DirectoryFactory(IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<MobileServiceProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
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
        });
    }
}
