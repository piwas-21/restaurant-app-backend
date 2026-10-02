using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantCataloguePublicationTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    private static TenantBridgeSettings Settings() => new()
    {
        Enabled = true,
        SyncAvailability = true,
        UseTenantCatalogue = true,
        Store = new()
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = Guid.NewGuid().ToString(),
            BaseUrl = "https://tenant.example/",
            Currency = "EUR",
            CatalogueApiToken = "catalogue-public-fixture",
            CatalogueRevision = "reviewed-selection-v1",
            Items = [new() { ProviderItemId = "sofra-test-meal-v1", ProductId = Guid.NewGuid() },
                new() { ProviderItemId = "sofra-test-drink-v1", ProductId = Guid.NewGuid() }]
        }
    };
    private static TenantCatalogueSnapshot Snapshot(TenantBridgeSettings settings, int meal = 675, string revision = "a", string reason = "")
        => new(new string(revision[0], 64), [new(settings.Store.Items[0].ProductId, null, "Actual tenant meal", "Independent description", null,
            reason.Length == 0 ? meal : null, reason.Length == 0, reason),
            new(settings.Store.Items[1].ProductId, null, "Actual tenant drink", "Actual drink description", null, 225, true, "")]);

    private Task<T> WithPublication<T>(TenantBridgeSettings settings, SourceClient tenant, Func<TenantCataloguePublication, JsonElement, PostgresCataloguePublications, Task<T>> action)
        => InScope(async services =>
        {
            await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
            var repository = new PostgresCataloguePublications(source);
            var publication = new TenantCataloguePublication(Options.Create(settings), Options.Create(new UberWebhookSettings
            { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] }), tenant, repository,
                new PostgresChannelAvailabilityJobs(source), new TenantMenuProvider(Options.Create(settings),
                Options.Create(new UberWebhookSettings { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] }),
                Provider, services.GetRequiredService<ISandboxTokens>()), TimeProvider.System);
            var template = services.GetRequiredService<ISandboxMenu>().Preview();
            if (Provider.Menu.GetProperty("menus").GetArrayLength() == 0) Provider.Menu = template;
            return await action(publication, template, repository);
        });
    private static AvailabilityBinding Binding(TenantBridgeSettings settings) => new(GatewayFixture.ClientId,
        settings.Store.StoreId, settings.Store.TenantId, settings.Store.CatalogueRevision);

    [Fact]
    public async Task SourceMenuIsDurablyPendingBeforeUploadAndVerifiedOnlyAfterIndependentReadback()
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Expected(default));
            var preview = await publication.Preview(template, default); Assert.True(preview.GetProperty("canPublish").GetBoolean());
            var revision = preview.GetProperty("revision").GetString()!;
            Provider.BeforeMenuUpload = async () =>
            {
                var pending = (await repository.Latest(Binding(settings), default))!;
                Assert.Equal("Pending", pending.State); Assert.Equal(revision, pending.Revision); Assert.Null(pending.VerifiedAt);
            };
            var result = await publication.Publish(template, revision, default); Assert.True(result.GetProperty("verified").GetBoolean());
            var row = (await repository.Latest(Binding(settings), default))!;
            Assert.Equal("Verified", row.State); Assert.Equal(revision, row.Revision); Assert.NotNull(row.VerifiedAt); Assert.NotNull(row.ProviderHash);
            Assert.Equal(675, Provider.Menu.GetProperty("items")[0].GetProperty("price_info").GetProperty("price").GetInt32());
            Assert.Equal("Actual tenant meal", Provider.Menu.GetProperty("items")[0].GetProperty("title").GetProperty("translations").GetProperty("en_us").GetString());
            Assert.Single(Provider.Calls, call => call.Method == HttpMethod.Put);
            await publication.Publish(template, revision, default); Assert.Single(Provider.Calls, call => call.Method == HttpMethod.Put);
            var expected = await publication.Expected(default); Assert.False(expected.GetProperty("items")[0].TryGetProperty("suspension_info", out _));
            return true;
        });
    }

    [Fact]
    public async Task DraftRevisionGuardRefusesStaleIntentBeforeDispatchAndAgainBeforeUpload()
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var preview = await publication.Preview(template, default);
            var revision = preview.GetProperty("revision").GetString()!;
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, revision, settings.Store,
                default, _ => Task.FromResult(false)));
            Assert.Empty(Provider.Calls);
            Assert.Null(await repository.Latest(Binding(settings), default));
            var checks = 0;
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, revision, settings.Store,
                default, _ => Task.FromResult(++checks < 2)));
            Assert.Equal(2, checks);
            Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Put);
            Assert.Equal("Pending", (await repository.Latest(Binding(settings), default))!.State);
            return true;
        });
    }

    [Fact]
    public async Task ReviewedDraftPromotesOnlyAfterReadbackAndDoesNotMutateBootstrapMapping()
    {
        var settings = Settings(); var originalProduct = settings.Store.Items[0].ProductId;
        var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var original = await publication.Preview(template, default);
            await publication.Publish(template, original.GetProperty("revision").GetString()!, default);
            var draft = Settings(); draft.Store.TenantId = settings.Store.TenantId;
            draft.Store.CatalogueRevision = "selected-tenant-menu-v2";
            tenant.Value = Snapshot(draft, 725, "b");
            var preview = await publication.Preview(template, draft.Store, default);
            Assert.Equal(originalProduct, settings.Store.Items[0].ProductId);
            Assert.Equal("reviewed-selection-v1", (await repository.Latest(Binding(settings), default))!
                .MappingSnapshot!.Value.GetProperty("catalogueRevision").GetString());
            await publication.Publish(template, preview.GetProperty("revision").GetString()!, draft.Store, default);
            Assert.Equal(originalProduct, settings.Store.Items[0].ProductId);
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var history = new PostgresCatalogueMappingHistory(database);
            Assert.NotNull(await history.FindVerified(Binding(settings), "reviewed-selection-v1", default));
            var active = (await history.FindVerified(Binding(settings), "selected-tenant-menu-v2", default))!;
            Assert.Equal(draft.Store.Items[0].ProductId.ToString(), active.MappingSnapshot!.Value
                .GetProperty("items")[0].GetProperty("productId").GetString());
            Assert.Equal(725, active.Menu.GetProperty("items")[0].GetProperty("price_info").GetProperty("price").GetInt32());
            draft.Store.StoreId = Guid.NewGuid();
            var callsBefore = Provider.Calls.Count;
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template,
                preview.GetProperty("revision").GetString()!, draft.Store, default));
            Assert.Equal(callsBefore, Provider.Calls.Count);
            return true;
        });
    }

    [Fact]
    public async Task ChangedSourceOrUnsupportedChoicesRefuseEveryProviderWrite()
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var preview = await publication.Preview(template, default); tenant.Value = Snapshot(settings, 725, "b");
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, preview.GetProperty("revision").GetString()!, default));
            tenant.Value = Snapshot(settings, reason: "UnsupportedChoices");
            var blocked = await publication.Preview(template, default); Assert.False(blocked.GetProperty("canPublish").GetBoolean());
            Assert.Equal("UnsupportedChoices", blocked.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("itemId").GetString() == "sofra-test-meal-v1").GetProperty("blockReason").GetString());
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, "", default));
            Assert.Empty(Provider.Calls); Assert.Null(await repository.Latest(Binding(settings), default)); return true;
        });
    }

    [Fact]
    public async Task LostUploadReplyRecoversFromCanonicalReadbackWithoutDuplicateReplacement()
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var preview = await publication.Preview(template, default); var revision = preview.GetProperty("revision").GetString()!;
            Provider.ThrowAfterMenuUpload = true;
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, revision, default));
            Assert.Equal("Pending", (await repository.Latest(Binding(settings), default))!.State);
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.RequireActive(default));
            Provider.ThrowAfterMenuUpload = false;
            await publication.Publish(template, revision, default);
            Assert.Single(Provider.Calls, call => call.Method == HttpMethod.Put);
            Assert.Equal("Verified", (await repository.Latest(Binding(settings), default))!.State); return true;
        });
    }

    [Fact]
    public async Task SourceChangesAfterPendingIntentCanRecoverWhenOldMenuIsIndependentlyConfirmed()
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var preview = await publication.Preview(template, default);
            tenant.AfterRead = call => { if (call == 3) tenant.Value = Snapshot(settings, 725, "b"); };
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, preview.GetProperty("revision").GetString()!, default));
            Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Put);
            Assert.Equal("Pending", (await repository.Latest(Binding(settings), default))!.State);
            var fresh = await publication.Preview(template, default);
            await publication.Publish(template, fresh.GetProperty("revision").GetString()!, default);
            Assert.Equal("Verified", (await repository.Latest(Binding(settings), default))!.State);
            await using var db = NpgsqlDataSource.Create(Database.ConnectionString);
            await using var check = db.CreateCommand("SELECT state FROM channel_catalogue_publications WHERE tenant_id = $1 ORDER BY sequence");
            check.Parameters.AddWithValue(settings.Store.TenantId); await using var rows = await check.ExecuteReaderAsync();
            Assert.True(await rows.ReadAsync()); Assert.Equal("Abandoned", rows.GetString(0));
            Assert.True(await rows.ReadAsync()); Assert.Equal("Verified", rows.GetString(0)); return true;
        });
    }

    [Theory]
    [InlineData("client")]
    [InlineData("override")]
    [InlineData("modifier")]
    [InlineData("price-shape")]
    [InlineData("stock-overrides")]
    [InlineData("stock-shape")]
    [InlineData("stock-timestamp")]
    [InlineData("stock-unknown-rule")]
    [InlineData("stock-reason-shape")]
    public async Task UnexpectedProviderGraphRefusesReplacement(string fault)
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot(settings));
        await WithPublication(settings, tenant, async (publication, template, repository) =>
        {
            var menu = JsonNode.Parse(template.GetRawText())!;
            if (fault == "client") Provider.MenuClientId = "foreign-client";
            if (fault == "override") menu["items"]![0]!["price_info"]!["overrides"] = JsonNode.Parse("""[{"price":0}]""");
            if (fault == "modifier") menu["items"]![0]!["modifier_group_ids"] = JsonNode.Parse("""{"ids":["choice"]}""");
            if (fault == "price-shape") menu["items"]![0]!["price_info"] = "invalid-price-shape";
            if (fault == "stock-overrides") menu["items"]![0]!["suspension_info"] = JsonNode.Parse("""{"overrides":[{"context_type":"MENU"}]}""");
            if (fault == "stock-shape") menu["items"]![0]!["suspension_info"] = "invalid-stock-shape";
            if (fault == "stock-timestamp") menu["items"]![0]!["suspension_info"] = JsonNode.Parse("""{"suspension":{"suspend_until":"tomorrow"}}""");
            if (fault == "stock-unknown-rule") menu["items"]![0]!["suspension_info"] = JsonNode.Parse("""{"schedule":[]}""");
            if (fault == "stock-reason-shape") menu["items"]![0]!["suspension_info"] = JsonNode.Parse("""{"suspension":{"reason":{}}}""");
            Provider.Menu = JsonSerializer.SerializeToElement(menu);
            var preview = await publication.Preview(template, default);
            await Assert.ThrowsAsync<ChannelConsoleException>(() => publication.Publish(template, preview.GetProperty("revision").GetString()!, default));
            Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Put);
            Assert.Null(await repository.Latest(Binding(settings), default)); return true;
        });
    }

    private sealed class SourceClient(TenantCatalogueSnapshot value) : ITenantCatalogueClient
    {
        public TenantCatalogueSnapshot Value { get; set; } = value;
        public Action<int>? AfterRead { get; set; }
        public int Calls { get; private set; }
        public Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
        { Calls++; AfterRead?.Invoke(Calls); return Task.FromResult(Value); }
    }
}
