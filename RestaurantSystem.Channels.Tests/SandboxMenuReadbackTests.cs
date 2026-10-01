using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class SandboxMenuReadbackTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    // Captured from Uber's sandbox GET after the two-item PUT, independent of our matcher/fake.
    private static JsonObject Readback() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "uber-sandbox-menu-readback.json")))!.AsObject();

    [Fact]
    public async Task CanonicalProviderReadbackVerifiesWithoutAnotherUploadAndAllowsMerchantEnable()
    {
        Provider.Menu = JsonSerializer.SerializeToElement(Readback());
        using var anonymous = await Client.GetAsync("/api/sandbox/uber/verification");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await Login();
        using var verified = await Client.GetAsync("/api/sandbox/uber/verification");
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.True((await Json(verified)).GetProperty("verified").GetBoolean());
        Assert.DoesNotContain(Provider.Calls, c => c.Method != HttpMethod.Get);
        var session = await Session();
        var url = await InScope(s => s.GetRequiredService<ISandboxConnection>().Start(session, default, true));
        await InScope(async s =>
        {
            await s.GetRequiredService<ISandboxConnection>().Complete(session,
                QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString(), "public-fixture-code", "", default);
            return true;
        });
        var promotion = Provider.Calls.Last(c => c.Method == HttpMethod.Post);
        Assert.False(promotion.Body!.Value.GetProperty("require_manual_acceptance").GetBoolean());
        Assert.DoesNotContain(Provider.Calls, c => c.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("items.0.price_info.price", "501")]
    [InlineData("items.0.title.translations.en_us", "\"Different title\"")]
    [InlineData("items.0.description.translations.en_us", "\"Different description\"")]
    [InlineData("items.0.tax_info.vat_rate_percentage", "21")]
    [InlineData("items.0.tax_info", "__REMOVE__")]
    [InlineData("items.1.id", "\"unexpected-item\"")]
    [InlineData("menus.0.service_availability.0.time_periods.0.end_time", "\"18:00\"")]
    [InlineData("menus.0.category_ids.0", "\"unexpected-category\"")]
    [InlineData("categories.0.entities.0.id", "\"unexpected-item\"")]
    [InlineData("categories.0.entities.0.type", "\"MODIFIER_GROUP\"")]
    [InlineData("categories.0.entities.0.type", "null")]
    [InlineData("categories.0.title.translations.en_us", "\"Different category\"")]
    [InlineData("modifier_groups", "[{\"id\":\"unexpected-modifier\"}]")]
    [InlineData("modifier_groups", "__REMOVE__")]
    public async Task NormalizationNeverHidesChangedOperationalFields(string path, string replacement)
    {
        var readback = Readback();
        Change(readback, path, replacement);
        Provider.Menu = JsonSerializer.SerializeToElement(readback);
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(async s =>
        {
            await s.GetRequiredService<ISandboxMenu>().RequireVerified(default);
            return true;
        }));
        Assert.Equal(409, error.Status);
        Assert.DoesNotContain(Provider.Calls, c => c.Method != HttpMethod.Get);
    }

    private static void Change(JsonNode node, string path, string replacement)
    {
        var parts = path.Split('.');
        foreach (var part in parts[..^1]) node = node is JsonArray array ? array[int.Parse(part)]! : node[part]!;
        var value = replacement == "__REMOVE__" ? null : JsonNode.Parse(replacement);
        if (node is JsonArray lastArray) lastArray[int.Parse(parts[^1])] = value;
        else if (replacement == "__REMOVE__") node.AsObject().Remove(parts[^1]);
        else node[parts[^1]] = value;
    }
}
