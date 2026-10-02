using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

public sealed class CatalogueMappingResolverTests
{
    [Fact]
    public async Task VerifiedMappingReplacesDeploymentSelectionWithoutMutatingItAndOldJobsRetainTheirMap()
    {
        var configured = Store("bootstrap", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var old = Store("mapping-one", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var current = Store("mapping-two", Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var records = new Records(Publication(current)); records.Historical = Publication(old);
        var resolver = Resolver(configured, records);
        var active = await resolver.Active(default);
        Assert.Equal(current.Items[0].ProductId, active.Items[0].ProductId);
        Assert.Equal("mapping-two", active.CatalogueRevision);
        Assert.Equal(configured.ApiToken, active.ApiToken);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), configured.Items[0].ProductId);
        var historical = await resolver.ForRevision("mapping-one", default);
        Assert.Equal(old.Items[0].ProductId, historical.Items[0].ProductId);
        Assert.Equal("mapping-one", records.RequestedRevision);
    }

    [Fact]
    public async Task PendingPublicationBlocksNewDiscoveryButDoesNotDestroyHistoricalVerifiedMappings()
    {
        var configured = Store("bootstrap", Guid.NewGuid());
        var previous = Publication(Store("previous", Guid.NewGuid()));
        var records = new Records(previous with { State = "Pending", ProviderHash = null, VerifiedAt = null })
        { Historical = previous };
        var resolver = Resolver(configured, records);
        await Assert.ThrowsAsync<ChannelConsoleException>(() => resolver.Active(default));
        Assert.Equal("previous", (await resolver.ForRevision("previous", default)).CatalogueRevision);
    }

    [Fact]
    public async Task SnapshotCannotChangeTenantOrReplaceIdentityWithoutItsReviewedHash()
    {
        var configured = Store("bootstrap", Guid.NewGuid());
        var publication = Publication(Store("reviewed", Guid.NewGuid()));
        var hostile = ProviderJson.Encode(new
        {
            catalogueRevision = "reviewed",
            items = new[]
            {
                new { providerItemId = "item", productId = Guid.NewGuid(), variationId = (Guid?)null, variationName = (string?)null }
            }
        });
        var records = new Records(publication with { MappingSnapshot = hostile });
        await Assert.ThrowsAsync<ChannelConsoleException>(() => Resolver(configured, records).Active(default));
    }

    [Fact]
    public async Task LegacyPublicationRequiresTheExactDeploymentMappingRatherThanGuessingFromNames()
    {
        var configured = Store("bootstrap", Guid.NewGuid());
        var records = new Records(Publication(configured) with { MappingSnapshot = null });
        Assert.Same(configured, await Resolver(configured, records).Active(default));
        records.LatestPublication = records.LatestPublication with { MappingHash = new string('f', 64) };
        await Assert.ThrowsAsync<ChannelConsoleException>(() => Resolver(configured, records).Active(default));
    }

    private static CatalogueMappingResolver Resolver(TenantStoreBinding configured, Records records)
        => new(Options.Create(new TenantBridgeSettings { UseTenantCatalogue = true, Store = configured }),
            Options.Create(new UberWebhookSettings { ClientId = "fixture-client" }), records, records);

    private static TenantStoreBinding Store(string revision, Guid product) => new()
    {
        StoreId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        TenantId = "fixture-tenant",
        BaseUrl = "https://tenant.example/",
        ApiToken = "public-fixture-token",
        Currency = "EUR",
        CatalogueRevision = revision,
        PublishedMenuHash = new string('a', 64),
        Items = [new() { ProviderItemId = "item", ProductId = product }]
    };

    private static CataloguePublication Publication(TenantStoreBinding store) => new(Guid.NewGuid(),
        CatalogueMenuPlanner.MappingHash(store), new string('b', 64), new string('c', 64),
        ProviderJson.Encode(new { items = Array.Empty<object>() }), ProviderJson.Encode(new { }),
        "Verified", new string('d', 64), DateTimeOffset.UtcNow,
        ProviderJson.Encode(new
        {
            catalogueRevision = store.CatalogueRevision,
            items = store.Items.Select(row => new
            {
                providerItemId = row.ProviderItemId,
                productId = row.ProductId,
                variationId = row.VariationId,
                variationName = row.VariationName
            })
        }));

    private sealed class Records(CataloguePublication publication) : ICataloguePublications, ICatalogueMappingHistory
    {
        public CataloguePublication LatestPublication { get; set; } = publication;
        public CataloguePublication? Historical { get; set; }
        public string? RequestedRevision { get; private set; }
        public Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<CataloguePublication?>(LatestPublication);
        public Task<CataloguePublication?> FindVerified(AvailabilityBinding binding, string catalogueRevision, CancellationToken cancellationToken)
        { RequestedRevision = catalogueRevision; return Task.FromResult(Historical); }
        public Task<CataloguePublication> Begin(AvailabilityBinding binding, CataloguePublicationIntent intent,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
