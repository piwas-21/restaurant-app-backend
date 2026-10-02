using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantChannelManagementOperationsTests
{
    private static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task FirstSaveCanReloadPreviewAndPublishWithoutAnExistingDraft()
    {
        var operations = CreateOperations();

        var saved = await operations.SaveDraft(new(null, [new("meal", ProductId, null)]), ActorId, default);
        var draftRevision = saved.GetProperty("draftRevision").GetString();
        Assert.True(Guid.TryParse(draftRevision, out _));
        Assert.Equal("currentReadback", saved.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());

        var catalogueView = await operations.Catalogue(default);
        Assert.Equal(draftRevision, catalogueView.GetProperty("draftRevision").GetString());
        Assert.Equal("currentReadback", catalogueView.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());
        Assert.Equal("reviewedTemplate", catalogueView.GetProperty("serviceHoursStatus").GetString());
        Assert.Equal("currentReadback", catalogueView.GetProperty("currentServiceHoursStatus").GetString());
        var preview = await operations.Preview(new(draftRevision!), ActorId, default);
        Assert.True(preview.GetProperty("canPublish").GetBoolean());
        Assert.False(preview.GetProperty("serviceHoursEditable").GetBoolean());

        var result = await operations.Publish(new(draftRevision!, preview.GetProperty("publicationRevision").GetString()!), ActorId, default);
        Assert.Equal("verified", result.GetProperty("state").GetString());
        Assert.True(result.GetProperty("providerReadbackVerified").GetBoolean());
        var publishedCatalogue = await operations.Catalogue(default);
        Assert.Equal("currentReadback", publishedCatalogue.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());
        Assert.Equal(725, publishedCatalogue.GetProperty("items")[0].GetProperty("providerPriceMinor").GetInt32());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("missing-item")]
    public async Task UnknownProviderMenuDoesNotInventPricesOrCrashDraftReview(string fault)
    {
        var operations = CreateOperations(fault);
        var saved = await operations.SaveDraft(new(null, [new("meal", ProductId, null)]), ActorId, default);
        var catalogue = await operations.Catalogue(default);
        var preview = await operations.Preview(new(saved.GetProperty("draftRevision").GetString()!), ActorId, default);
        foreach (var response in new[] { saved, catalogue, preview })
        {
            var item = response.GetProperty("items")[0];
            Assert.Equal(JsonValueKind.Null, item.GetProperty("providerPriceMinor").ValueKind);
            Assert.Equal("unknown", item.GetProperty("providerPriceStatus").GetString());
            Assert.Equal(725, item.GetProperty("tenantPriceMinor").GetInt32());
        }
        Assert.Equal("reviewedTemplate", preview.GetProperty("serviceHoursStatus").GetString());
        Assert.Equal("unknown", preview.GetProperty("currentServiceHoursStatus").GetString());
    }

    [Fact]
    public async Task ImportExceptionWireHasActualUpdateTimeAndCanBeOpenedByItsIdentity()
    {
        var olderOrder = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var newerOrder = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var created = DateTimeOffset.Parse("2026-10-01T09:00:00Z");
        var updated = DateTimeOffset.Parse("2026-10-02T09:00:00Z");
        var imports = ProviderJson.Encode(new
        {
            items = new[]
        {
            new { orderId = olderOrder, state = "Quarantined", code = "PayloadExpired", createdAt = created, updatedAt = updated },
            new { orderId = newerOrder, state = "Quarantined", code = "ContractRejected", createdAt = created.AddHours(1), updatedAt = updated.AddHours(-1) }
        },
            truncated = false
        });
        var operations = CreateOperations(imports: imports);
        var inbox = await operations.Exceptions("", default);
        var first = inbox.GetProperty("items")[0];
        Assert.Equal(olderOrder, first.GetProperty("id").GetGuid());
        Assert.Equal(created, first.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(updated, first.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.False(first.GetProperty("canReconcile").GetBoolean());
        Assert.False(first.TryGetProperty("OperationId", out _));
        Assert.False(first.TryGetProperty("operationId", out _));
        var detail = await operations.Exception(olderOrder, default);
        Assert.Equal(first.GetRawText(), detail.GetRawText());
    }

    private static TenantChannelManagementOperations CreateOperations(string readFault = "", JsonElement? imports = null)
    {
        var store = new TenantStoreBinding
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = "management-tenant",
            BaseUrl = "https://tenant.example/",
            ApiToken = "order-ingress-fixture",
            CatalogueApiToken = "catalogue-fixture",
            Currency = "EUR",
            CatalogueRevision = "deployment-revision",
            PublishedMenuHash = new string('a', 64),
            Items = [new() { ProviderItemId = "meal", ProductId = Guid.Parse("30000000-0000-0000-0000-000000000001") }]
        };
        var drafts = new Drafts(); var publications = new Publications(); var jobs = new Jobs();
        var catalogue = new Source(); var menuProvider = new MenuProvider(Menu());
        var webhook = Options.Create(new UberWebhookSettings { ClientId = "sandbox-client", StoreIds = [store.StoreId] });
        var bridge = Options.Create(new TenantBridgeSettings
        {
            Enabled = true,
            UseTenantCatalogue = true,
            SyncAvailability = true,
            Store = store
        });
        var publication = new TenantCataloguePublication(bridge, webhook, catalogue, publications, jobs,
            menuProvider, TimeProvider.System, new Resolver(store));
        var operations = new TenantChannelManagementOperations(bridge,
            Options.Create(new TenantManagementGatewaySettings { Enabled = true }), webhook, new Connection(),
            new SandboxMenuStub(Menu(), menuProvider) { ReadFault = readFault }, catalogue, new OAuthFlows(), publication, publications,
            new Resolver(store), drafts, new AvailabilityStatus(), new Overrides(), jobs, new Audit(), new Imports(imports),
            new Source(), new UberAvailability(), new ConnectionState(), TimeProvider.System);

        return operations;
    }

    private static JsonElement Menu() => JsonDocument.Parse("""
        {"menus":[{"id":"menu","service_availability":[{"day_of_week":"monday","time_periods":[{"start_time":"09:00","end_time":"17:00"}]}]}],
         "categories":[],"items":[{"id":"meal","title":{"translations":{"en_us":"Old name"}},
         "description":{"translations":{"en_us":"Old description"}},"price_info":{"price":500},
         "tax_info":{"vat_rate_percentage":9}}],"modifier_groups":[]}
        """).RootElement.Clone();

    private sealed class Source : ITenantCatalogueClient, ITenantAvailabilityClient
    {
        private static readonly string Revision = new('b', 64);
        public Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
            => Task.FromResult(new TenantCatalogueSnapshot(Revision, store.Items.Select(item =>
                new TenantCatalogueItem(item.ProductId, item.VariationId, "Sofra meal", "Reviewed description",
                    item.VariationName, 725, true, "")).ToArray()));
        Task<TenantAvailabilitySnapshot> ITenantAvailabilityClient.Read(TenantStoreBinding store, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAvailabilitySnapshot(Revision, store.Items.Select(item =>
                new TenantAvailabilityItem(item.ProviderItemId, true, "Available")).ToArray()));
    }

    private sealed class MenuProvider(JsonElement menu) : ITenantMenuProvider
    {
        private JsonElement _menu = menu;
        public Task<JsonElement> Read(CancellationToken cancellationToken) => Task.FromResult(_menu.Clone());
        public Task Upload(JsonElement value, CancellationToken cancellationToken) { _menu = value.Clone(); return Task.CompletedTask; }
    }

    private sealed class SandboxMenuStub(JsonElement menu, MenuProvider provider) : ISandboxMenu
    {
        public JsonElement Preview() => menu.Clone();
        public string ReadFault { get; init; } = string.Empty;
        public Task<JsonElement> Read(CancellationToken cancellationToken) => ReadFault switch
        {
            "unavailable" => Task.FromException<JsonElement>(new ChannelConsoleException(502, "Provider unavailable")),
            "missing-item" => Task.FromResult(JsonDocument.Parse("{\"menus\":[],\"items\":[]}").RootElement.Clone()),
            _ => provider.Read(cancellationToken)
        };
        public Task<JsonElement> Publish(CancellationToken cancellationToken) => Task.FromResult(menu.Clone());
        public Task<JsonElement> Publish(string revision, CancellationToken cancellationToken) => Publish(cancellationToken);
        public Task RequireVerified(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement> Expected(CancellationToken cancellationToken) => Task.FromResult(menu.Clone());
    }

    private sealed class Drafts : ICatalogueMappingDrafts
    {
        private CatalogueMappingDraft? _draft;
        public Task<CatalogueMappingDraft?> Read(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult(_draft);
        public Task<bool> Save(AvailabilityBinding binding, CatalogueMappingDraft draft, string? expectedRevision, CancellationToken cancellationToken)
        {
            if (_draft?.Revision != expectedRevision) return Task.FromResult(false);
            _draft = draft; return Task.FromResult(true);
        }
    }

    private sealed class Publications : ICataloguePublications
    {
        private CataloguePublication? _latest;
        public Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult(_latest);
        public Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_latest?.Id == id ? _latest : null);
        public Task<CataloguePublication> Begin(AvailabilityBinding binding, string mappingHash, string sourceRevision,
            string revision, JsonElement menu, JsonElement previousMenu, CancellationToken cancellationToken, JsonElement? mappingSnapshot = null)
            => Task.FromResult(_latest = new(Guid.NewGuid(), mappingHash, sourceRevision, revision, menu, previousMenu,
                CataloguePublicationStates.Pending, null, null, mappingSnapshot));
        public Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (_latest?.Id != id) return Task.FromResult(false);
            _latest = _latest with { State = CataloguePublicationStates.Verified, ProviderHash = providerHash, VerifiedAt = now };
            return Task.FromResult(true);
        }
        public Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
        {
            if (_latest?.Id != id) return Task.FromResult(false);
            _latest = _latest with { State = CataloguePublicationStates.Abandoned }; return Task.FromResult(true);
        }
    }

    private sealed class Resolver(TenantStoreBinding store) : ICatalogueMappingResolver
    {
        public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => Task.FromResult(store);
        public Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken) => Task.FromResult(store);
    }

    private sealed class Jobs : IChannelAvailabilityJobs
    {
        public Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<IChannelAvailabilityLease?>(new Lease());
        public Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChannelAvailabilityState>>([]);
    }

    private sealed class Lease : IChannelAvailabilityLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<bool> Queue(string sourceRevision, IReadOnlyList<ChannelAvailabilityDesired> items, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> Observe(string itemId, string sourceRevision, string state, bool? observedAvailable, string? providerHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Audit : IChannelManagementAudit
    {
        public Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode, Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding, long? beforeSequence, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChannelManagementAuditRecord>>([]);
    }

    private sealed class OAuthFlows : ITenantOAuthFlows
    {
        public Task Create(TenantOAuthFlow flow, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TenantOAuthFlow?> Read(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task<TenantOAuthFlow?> FindByStateHash(AvailabilityBinding binding, string stateHash, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task Expire(AvailabilityBinding binding, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TenantOAuthFlow?> Claim(AvailabilityBinding binding, string stateHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task<bool> Finish(AvailabilityBinding binding, Guid id, string status, string? errorCode, DateTimeOffset completedAt, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Overrides : IChannelAvailabilityOverrides
    {
        public Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult<ChannelAvailabilityOverride?>(null);
        public Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused, DateTimeOffset? pausedUntil, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelAvailabilityOverride(isPaused, pausedUntil, actorId, now));
    }

    private sealed class ConnectionState : IChannelManagementConnectionState
    {
        public Task<ChannelManagementConnectionState> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelManagementConnectionState(false, null, null));
        public Task<ChannelManagementConnectionState> Set(AvailabilityBinding binding, bool isDisconnected, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelManagementConnectionState(isDisconnected, actorId, now));
    }

    private sealed class AvailabilityStatus : IChannelAvailabilityStatus
    {
        public Task<JsonElement> Read(CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { enabled = true, paused = false, items = Array.Empty<object>() }));
    }

    private sealed class Imports(JsonElement? body = null) : IChannelImportView
    {
        public Task<JsonElement> Read(string cursor, CancellationToken cancellationToken) => Task.FromResult(body ?? ProviderJson.Encode(new { items = Array.Empty<object>(), truncated = false }));
    }

    private sealed class UberAvailability : IUberAvailabilityClient
    {
        public Task<UberAvailabilitySnapshot> Read(TenantStoreBinding store, string clientId, CancellationToken cancellationToken)
            => Task.FromResult(new UberAvailabilitySnapshot("hash", new Dictionary<string, bool>()));
        public Task Update(TenantStoreBinding store, TenantAvailabilityItem item, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Connection : ISandboxConnection
    {
        public Task<string> Start(string sessionHash, CancellationToken cancellationToken, bool enableTesting = false) => Task.FromResult(string.Empty);
        public Task Complete(string sessionHash, string state, string code, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement> ConnectTenant(string merchantToken, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> ConnectTenant(string merchantToken, bool enableOrderAcceptance, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> Configuration(CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> EnableOrders(bool enable, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
    }
}
