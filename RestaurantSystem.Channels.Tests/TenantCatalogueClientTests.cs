using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantCatalogueClientTests
{
    private static readonly Guid Product = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static TenantStoreBinding Store() => new()
    {
        StoreId = GatewayFixture.StoreId,
        Currency = "EUR",
        BaseUrl = "https://tenant.example/",
        CatalogueApiToken = "catalogue-public-fixture",
        ApiToken = "order-ingress-public-fixture",
        Items = [new() { ProviderItemId = "meal", ProductId = Product }]
    };
    private static JsonNode Reply()
    {
        var item = new ApiCatalogueItem(Product, null, "Tenant meal", "Actual description", null, 525, true, "");
        var revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Provider = "uber-eats",
            StoreId = GatewayFixture.StoreId.ToString("D"),
            Currency = "EUR",
            IsSandbox = true,
            Language = "en",
            Items = new[] { item }
        })));
        return JsonSerializer.SerializeToNode(new
        {
            provider = "uber-eats",
            storeId = GatewayFixture.StoreId.ToString("D"),
            currency = "EUR",
            isSandbox = true,
            language = "en",
            revision,
            items = new[] { new { productId = Product, variationId = (Guid?)null,
                name = "Tenant meal", description = "Actual description", variationName = (string?)null, priceMinor = 525, available = true, blockReason = "" } }
        })!;
    }

    private sealed record ApiCatalogueItem(Guid ProductId, Guid? VariationId, string Name, string Description,
        string? VariationName, int? PriceMinor, bool Available, string BlockReason);

    [Fact]
    public async Task TenantApiRevisionForItsEightFieldSnapshotIsAccepted()
    {
        var store = Store(); var transport = new Transport(Reply());
        var reply = await new TenantCatalogueClient(transport).Read(store, default);
        var item = Assert.Single(reply.Items);
        Assert.Equal(525, item.PriceMinor);
        Assert.Equal("Actual description", item.Description);
        Assert.Empty(item.SelectionKey);
        Assert.Null(item.CategoryId);
        Assert.Empty(item.SourceFingerprint);
        Assert.Equal("catalogue-public-fixture", transport.Credential);
        Assert.Equal("/api/delivery-channels/catalogue/snapshot", transport.Path);
        Assert.Equal("order-ingress-public-fixture", store.ApiToken);
        Assert.Equal(Product, transport.Request.GetProperty("items")[0].GetProperty("productId").GetGuid());
    }

    [Theory]
    [InlineData("store")]
    [InlineData("client-body")]
    [InlineData("environment")]
    [InlineData("currency")]
    [InlineData("language")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("unknown-product")]
    [InlineData("price-string")]
    [InlineData("negative-price")]
    [InlineData("missing-reason")]
    [InlineData("unknown-reason")]
    [InlineData("missing-variation")]
    [InlineData("changed-text-same-revision")]
    public async Task IncompleteOrTamperedSnapshotsCannotBecomePublishable(string fault)
    {
        var reply = Reply(); var row = reply["items"]![0]!;
        switch (fault)
        {
            case "store": reply["storeId"] = Guid.NewGuid().ToString(); break;
            case "client-body": reply["provider"] = "different-provider"; break;
            case "environment": reply["isSandbox"] = false; break;
            case "currency": reply["currency"] = "CHF"; break;
            case "language": reply["language"] = "nl"; break;
            case "duplicate": reply["items"]!.AsArray().Add(row.DeepClone()); break;
            case "missing": reply["items"]!.AsArray().Clear(); break;
            case "unknown-product": row["productId"] = Guid.NewGuid().ToString(); break;
            case "price-string": row["priceMinor"] = "525"; break;
            case "negative-price": row["priceMinor"] = -1; break;
            case "missing-reason": row.AsObject().Remove("blockReason"); break;
            case "unknown-reason": row["blockReason"] = "secret-token-fixture"; break;
            case "missing-variation": row.AsObject().Remove("variationId"); break;
            case "changed-text-same-revision": row["description"] = "Stale revision cannot hide an edit"; break;
        }
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => new TenantCatalogueClient(new Transport(reply)).Read(Store(), default));
        Assert.Equal(502, error.Status); Assert.DoesNotContain("secret-token", error.Message);
    }

    private sealed class Transport(JsonNode reply) : ITenantChannelTransport
    {
        public string Credential { get; private set; } = "";
        public string Path { get; private set; } = "";
        public JsonElement Request { get; private set; }
        public Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken)
        {
            Credential = store.ApiToken; Path = path; Request = JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(reply));
        }
    }
}
