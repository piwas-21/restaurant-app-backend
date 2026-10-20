using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobileAssociationHttpTests
{
    [Theory]
    [InlineData("apple-app-site-association")]
    [InlineData("assetlinks.json")]
    public async Task PublicDocumentUsesOnlyReviewedIdentitiesAndExactWireFields(string file)
    {
        await using var factory = new MobileLinksFactory(associations: MobileAssociationFixture.Settings());
        using var client = factory.CreateClient(new() { BaseAddress = new("https://links.example"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/.well-known/" + file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        if (file == "apple-app-site-association")
        {
            Assert.Equal(["applinks"], root.EnumerateObject().Select(value => value.Name).ToArray());
            var details = root.GetProperty("applinks").GetProperty("details");
            Assert.Equal(1, details.GetArrayLength());
            var app = details[0];
            Assert.Equal(["appIDs", "components"], app.EnumerateObject().Select(value => value.Name).ToArray());
            Assert.Equal([MobileAssociationFixture.AppleId], app.GetProperty("appIDs").EnumerateArray().Select(value => Assert.IsType<string>(value.GetString())).ToArray());
            var component = app.GetProperty("components")[0];
            Assert.Equal(["/", "?"], component.EnumerateObject().Select(value => value.Name).ToArray());
            Assert.Equal("/r/*/payment-return/*", component.GetProperty("/").GetString());
            Assert.Equal("*", component.GetProperty("?").GetProperty("operation").GetString());
        }
        else
        {
            Assert.Equal(1, root.GetArrayLength());
            var statement = root[0];
            Assert.Equal(["relation", "target"], statement.EnumerateObject().Select(value => value.Name).ToArray());
            Assert.Equal(["delegate_permission/common.handle_all_urls"], statement.GetProperty("relation").EnumerateArray().Select(value => Assert.IsType<string>(value.GetString())).ToArray());
            var target = statement.GetProperty("target");
            Assert.Equal(["namespace", "package_name", "sha256_cert_fingerprints"], target.EnumerateObject().Select(value => value.Name).ToArray());
            Assert.Equal("android_app", target.GetProperty("namespace").GetString());
            Assert.Equal(MobileAssociationFixture.AndroidPackage, target.GetProperty("package_name").GetString());
            Assert.Equal([MobileAssociationFixture.Fingerprint], target.GetProperty("sha256_cert_fingerprints").EnumerateArray().Select(value => Assert.IsType<string>(value.GetString())).ToArray());
        }
    }

    [Theory]
    [InlineData("apple-app-site-association", false)]
    [InlineData("assetlinks.json", false)]
    [InlineData("apple-app-site-association", true)]
    [InlineData("assetlinks.json", true)]
    public async Task DisabledOrWrongHostCannotPublishAnAssociation(string file, bool enabled)
    {
        await using var factory = new MobileLinksFactory(associations: MobileAssociationFixture.Settings(enabled));
        using var client = factory.CreateClient(new() { BaseAddress = new(enabled ? "https://other.example" : "https://links.example") });
        using var response = await client.GetAsync("/.well-known/" + file);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
