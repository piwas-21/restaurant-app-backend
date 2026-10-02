using System.Text.Json;
using System.Text.Json.Nodes;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantAvailabilityClientTests
{
    private static readonly Guid Meal = Guid.Parse("1d494e63-5f13-4a8f-b2a0-833da623ad13");
    private static readonly Guid Drink = Guid.Parse("18c77f81-850a-4c83-814a-ce0445ddb40c");
    private static TenantStoreBinding Store() => new()
    {
        StoreId = GatewayFixture.StoreId,
        BaseUrl = "https://tenant.example/",
        Currency = "EUR",
        ApiToken = "order-ingress-public-fixture",
        CatalogueApiToken = "catalogue-read-public-fixture",
        Items = [new() { ProviderItemId = "meal-v1", ProductId = Meal }, new() { ProviderItemId = "drink-v1", ProductId = Drink }],
    };

    [Fact]
    public async Task UsesOnlyDedicatedReadTokenAndMapsByIdentityInsteadOfReplyOrder()
    {
        var store = Store(); var transport = new RecordingTransport(Reply());
        var snapshot = await new TenantAvailabilityClient(transport).Read(store, default);
        Assert.Equal(new string('a', 64), snapshot.Revision);
        Assert.Collection(snapshot.Items,
            item => { Assert.Equal("meal-v1", item.ProviderItemId); Assert.True(item.Available); },
            item => { Assert.Equal("drink-v1", item.ProviderItemId); Assert.False(item.Available); Assert.Equal("UnavailableProduct", item.Reason); });
        Assert.Equal("/api/delivery-channels/catalogue/availability", transport.Path);
        Assert.Equal(store.BaseUrl, transport.Origin); Assert.Equal(store.CatalogueApiToken, transport.Token);
        Assert.Equal("order-ingress-public-fixture", store.ApiToken);
        Assert.Equal("uber-eats", transport.Body.GetProperty("provider").GetString());
        Assert.True(transport.Body.GetProperty("isSandbox").GetBoolean());
        Assert.Equal(2, transport.Body.GetProperty("items").GetArrayLength());
        Assert.False(transport.Body.TryGetProperty("apiToken", out _));
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("store")]
    [InlineData("currency")]
    [InlineData("environment")]
    [InlineData("missing-environment")]
    [InlineData("revision")]
    [InlineData("missing-item")]
    [InlineData("extra-item")]
    [InlineData("duplicate")]
    [InlineData("foreign-product")]
    [InlineData("foreign-variation")]
    [InlineData("empty-product")]
    [InlineData("missing-variation")]
    [InlineData("missing-available")]
    [InlineData("string-available")]
    [InlineData("unknown-reason")]
    [InlineData("contradictory-reason")]
    [InlineData("null-items")]
    public async Task IncompleteUnboundOrAmbiguousSnapshotCannotDriveProviderChanges(string fault)
    {
        var json = JsonNode.Parse(Reply().GetRawText())!.AsObject();
        var rows = json["items"]!.AsArray();
        switch (fault)
        {
            case "provider": json["provider"] = "foreign-provider"; break;
            case "store": json["storeId"] = Guid.NewGuid().ToString(); break;
            case "currency": json["currency"] = "CHF"; break;
            case "environment": json["isSandbox"] = false; break;
            case "missing-environment": json.Remove("isSandbox"); break;
            case "revision": json["revision"] = new string('A', 64); break;
            case "missing-item": rows.RemoveAt(0); break;
            case "extra-item": rows.Add(rows[0]!.DeepClone()); break;
            case "duplicate": rows[1] = rows[0]!.DeepClone(); break;
            case "foreign-product": rows[0]!["productId"] = Guid.NewGuid().ToString(); break;
            case "foreign-variation": rows[0]!["variationId"] = Guid.NewGuid().ToString(); break;
            case "empty-product": rows[0]!["productId"] = Guid.Empty.ToString(); break;
            case "missing-variation": rows[0]!.AsObject().Remove("variationId"); break;
            case "missing-available": rows[0]!.AsObject().Remove("available"); break;
            case "string-available": rows[0]!["available"] = "false"; break;
            case "unknown-reason": rows[0]!["reason"] = "provider-raw-message"; break;
            case "contradictory-reason": rows[0]!["reason"] = "Available"; break;
            case "null-items": json["items"] = null; break;
        }
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() =>
            new TenantAvailabilityClient(new RecordingTransport(JsonSerializer.SerializeToElement(json))).Read(Store(), default));
        Assert.Equal(502, error.Status);
    }

    [Fact]
    public async Task MissingReadTokenRefusesBeforeEgressAndNeverUsesIngressTokenAsFallback()
    {
        var store = Store(); store.CatalogueApiToken = string.Empty;
        var transport = new RecordingTransport(Reply());
        Assert.Equal(409, (await Assert.ThrowsAsync<ChannelConsoleException>(() =>
            new TenantAvailabilityClient(transport).Read(store, default))).Status);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task MultiplePublishedIdsForOneSelectionShareOneSourceIdentity()
    {
        var store = Store(); store.Items[0].ProductId = Drink;
        var json = JsonNode.Parse(Reply().GetRawText())!; json["items"]!.AsArray().RemoveAt(1);
        var transport = new RecordingTransport(JsonSerializer.SerializeToElement(json));
        var snapshot = await new TenantAvailabilityClient(transport).Read(store, default);
        Assert.Equal(1, transport.Body.GetProperty("items").GetArrayLength());
        Assert.Equal(2, snapshot.Items.Count); Assert.All(snapshot.Items, item => Assert.False(item.Available));
    }

    private static JsonElement Reply() => ProviderJson.Encode(new
    {
        provider = "uber-eats",
        storeId = GatewayFixture.StoreId.ToString(),
        currency = "EUR",
        isSandbox = true,
        revision = new string('a', 64),
        items = new[]
        {
            new { productId = Drink, variationId = (Guid?)null, available = false, reason = "UnavailableProduct" },
            new { productId = Meal, variationId = (Guid?)null, available = true, reason = "Available" },
        },
    });

    private sealed class RecordingTransport(JsonElement reply) : ITenantChannelTransport
    {
        public string? Token { get; private set; }
        public string? Origin { get; private set; }
        public string? Path { get; private set; }
        public JsonElement Body { get; private set; }
        public int Calls { get; private set; }
        public Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken)
        {
            Calls++; Token = store.ApiToken; Origin = store.BaseUrl; Path = path;
            Body = JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult<JsonElement?>(reply);
        }
    }
}
