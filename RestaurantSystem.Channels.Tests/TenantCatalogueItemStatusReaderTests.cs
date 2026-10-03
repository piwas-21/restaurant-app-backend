using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantCatalogueItemStatusReaderTests
{
    private static readonly Guid StoreId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private const int RevisionLength = 64;
    private static readonly string Revision = new('a', RevisionLength);

    [Fact]
    public async Task CategoryCheckParsesFreshSupportStatusForNegativeOverride()
    {
        var client = new TenantCatalogueClient(new ReplyTransport(JsonDocument.Parse($$"""
            {
              "provider":"uber-eats","storeId":"{{StoreId:D}}","currency":"EUR","isSandbox":true,
              "language":"en","revision":"{{Revision}}","categoryBasis":"primaryCategory",
              "maximumCategoryCount":1000,"maximumItemOverrideCount":2000,
              "categories":[],"sourceChanged":false,"removedCategoryIds":[],
              "removedItems":[],"removedItemOverrides":[],
              "itemStatuses":[{"selectionKey":"{{ProductId:D}}:base","productId":"{{ProductId:D}}",
                "variationId":null,"categoryId":"{{CategoryId:D}}","currentCategoryId":"{{CategoryId:D}}",
                "supported":false}]
            }
            """).RootElement.Clone()));
        var store = new TenantStoreBinding
        {
            StoreId = StoreId,
            Currency = "EUR",
            CatalogueApiToken = "catalogue-read",
            BaseUrl = "https://tenant.example/"
        };

        var result = await client.Categories(store, Revision, [], [],
            [new(ProductId, null, CategoryId, false)], default);

        var status = Assert.Single(result.ItemStatuses);
        Assert.Equal(ProductId, status.ProductId);
        Assert.Null(status.VariationId);
        Assert.Equal(CategoryId, status.CategoryId);
        Assert.Equal(CategoryId, status.CurrentCategoryId);
        Assert.False(status.Supported);
    }

    private sealed class ReplyTransport(JsonElement reply) : ITenantChannelTransport
    {
        public Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body,
            CancellationToken cancellationToken) => Task.FromResult<JsonElement?>(reply);
    }
}
