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
public sealed class ChannelAvailabilityProcessorTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    private static TenantBridgeSettings Settings() => new()
    {
        Enabled = true,
        SyncAvailability = true,
        Store = new()
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = Guid.NewGuid().ToString(),
            CatalogueRevision = "sandbox-menu-v1",
            CatalogueApiToken = "catalogue-read-public-fixture",
            Items = [new() { ProviderItemId = "sofra-test-meal-v1" }, new() { ProviderItemId = "sofra-test-drink-v1" }]
        },
    };
    private static TenantAvailabilitySnapshot Snapshot(bool meal = false, bool drink = true) => new(new string('a', 64),
        [new("sofra-test-meal-v1", meal, meal ? "Available" : "UnavailableProduct"),
         new("sofra-test-drink-v1", drink, drink ? "Available" : "UnavailableProduct")]);
    private void SeedMenu() => Provider.Menu = JsonSerializer.SerializeToElement(JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "uber-sandbox-menu-readback.json")))!);

    private Task<bool> Process(TenantBridgeSettings settings, SourceClient? tenant = null) => InScope(async services =>
    {
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        await new ChannelAvailabilityProcessor(Options.Create(settings), Options.Create(new UberWebhookSettings
        { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] }), tenant ?? new(Snapshot()),
            new UberAvailabilityClient(Provider, services.GetRequiredService<ISandboxTokens>(), services.GetRequiredService<ISandboxMenu>(), TimeProvider.System),
            new PostgresChannelAvailabilityJobs(source), TimeProvider.System).Process(default);
        return true;
    });
    private async Task<IReadOnlyList<ChannelAvailabilityState>> States(TenantBridgeSettings settings)
    {
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        return await new PostgresChannelAvailabilityJobs(source).Read(new(GatewayFixture.ClientId, settings.Store.StoreId,
            settings.Store.TenantId, settings.Store.CatalogueRevision), default);
    }

    [Fact]
    public async Task SparseStockWriteIsReadBackAndRepeatedCycleMakesNoDuplicateWrite()
    {
        SeedMenu(); var settings = Settings(); await Process(settings); await Process(settings);
        var write = Assert.Single(Provider.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal($"/v2/eats/stores/{GatewayFixture.StoreId:D}/menus/items/sofra-test-meal-v1", write.Path);
        Assert.Equal("suspension_info", Assert.Single(write.Body!.Value.EnumerateObject()).Name);
        Assert.Equal(2147483647, write.Body.Value.GetProperty("suspension_info").GetProperty("suspension").GetProperty("suspend_until").GetInt32());
        Assert.Equal(3, Provider.Calls.Count(call => call.Method == HttpMethod.Get));
        Assert.All(await States(settings), state => { Assert.Equal("Verified", state.State); Assert.NotNull(state.VerifiedAt); Assert.NotNull(state.ProviderHash); });
        Assert.Equal(500, Provider.Menu.GetProperty("items")[0].GetProperty("price_info").GetProperty("price").GetInt32());
        Assert.Equal(200, Provider.Menu.GetProperty("items")[1].GetProperty("price_info").GetProperty("price").GetInt32());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("not-opted-in")]
    [InlineData("wrong-store")]
    [InlineData("missing-read-token")]
    public async Task UnapprovedOrInactiveWorkerCannotFetchOrWrite(string mode)
    {
        var settings = Settings(); var tenant = new SourceClient(Snapshot());
        if (mode == "disabled") settings.Enabled = false;
        if (mode == "paused") settings.Paused = true;
        if (mode == "not-opted-in") settings.SyncAvailability = false;
        if (mode == "wrong-store") settings.Store.StoreId = Guid.NewGuid();
        if (mode == "missing-read-token") settings.Store.CatalogueApiToken = "";
        if (mode is "wrong-store" or "missing-read-token") await Assert.ThrowsAsync<ChannelConsoleException>(() => Process(settings, tenant));
        else await Process(settings, tenant);
        Assert.Equal(0, tenant.Calls); Assert.Empty(Provider.Calls); Assert.Empty(Provider.Grants);
    }

    [Theory]
    [InlineData("wrong-client")]
    [InlineData("changed-price")]
    [InlineData("duplicate-item")]
    [InlineData("context-override")]
    [InlineData("invalid-timestamp")]
    [InlineData("price-override")]
    [InlineData("bundle")]
    [InlineData("modifier")]
    [InlineData("quantity")]
    public async Task InvalidCanonicalGraphRefusesEveryWriteAndExposesUncertainty(string mutation)
    {
        SeedMenu(); var settings = Settings(); var menu = JsonNode.Parse(Provider.Menu.GetRawText())!;
        if (mutation == "wrong-client") Provider.MenuClientId = "foreign-client";
        if (mutation == "changed-price") menu["items"]![0]!["price_info"]!["price"] = 501;
        if (mutation == "duplicate-item") menu["items"]![1]!["id"] = "sofra-test-meal-v1";
        if (mutation == "context-override") menu["items"]![1]!["suspension_info"] = JsonNode.Parse("{\"overrides\":[{\"context_type\":\"MENU\"}]}");
        if (mutation == "invalid-timestamp") menu["items"]![1]!["suspension_info"] = JsonNode.Parse("{\"suspension\":{\"suspend_until\":\"tomorrow\"}}");
        if (mutation == "price-override") menu["items"]![1]!["price_info"]!["overrides"] = JsonNode.Parse("[{\"context_type\":\"MENU\",\"price\":0}]");
        if (mutation == "bundle") menu["items"]![1]!["bundled_items"] = JsonNode.Parse("[{\"id\":\"meal\"}]");
        if (mutation == "modifier") menu["items"]![1]!["modifier_group_ids"] = JsonNode.Parse("{\"ids\":[\"unknown-choice\"]}");
        if (mutation == "quantity") menu["items"]![1]!["quantity_info"] = JsonNode.Parse("{\"quantity\":{\"max_permitted\":2}}");
        Provider.Menu = JsonSerializer.SerializeToElement(menu);
        await Assert.ThrowsAsync<ChannelConsoleException>(() => Process(settings));
        Assert.DoesNotContain(Provider.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(2, (await States(settings)).Count);
        Assert.All(await States(settings), state => { Assert.Equal("Uncertain", state.State); Assert.Null(state.VerifiedAt); });
    }

    [Fact]
    public async Task SuccessfulResponseWithWrongReadbackRemainsMismatch()
    {
        SeedMenu(); Provider.IgnoreStockUpdate = true; var settings = Settings(); await Process(settings);
        var meal = (await States(settings)).Single(row => row.ProviderItemId == "sofra-test-meal-v1");
        Assert.Equal("Mismatch", meal.State); Assert.Null(meal.VerifiedAt); Assert.True(meal.ObservedAvailable);
    }

    [Fact]
    public async Task AmbiguousWriteRecoversAfterRestartFromReadbackWithoutAnotherWrite()
    {
        SeedMenu(); Provider.ThrowAfterStockUpdate = true; var settings = Settings();
        await Assert.ThrowsAsync<ChannelConsoleException>(() => Process(settings));
        var meal = (await States(settings)).Single(row => row.ProviderItemId == "sofra-test-meal-v1");
        Assert.Equal("Uncertain", meal.State); Assert.Null(meal.VerifiedAt);
        Provider.ThrowAfterStockUpdate = false; await Process(settings);
        Assert.Single(Provider.Calls, call => call.Method == HttpMethod.Post);
        Assert.All(await States(settings), row => Assert.Equal("Verified", row.State));
    }

    [Fact]
    public async Task BoundedCyclePersistsRemainingPendingAndNextCycleFinishes()
    {
        SeedMenu(); var settings = Settings(); settings.AvailabilityMaxWrites = 1;
        var tenant = new SourceClient(Snapshot(false, false)); await Process(settings, tenant);
        var rows = await States(settings); Assert.Single(rows, row => row.State == "Verified"); Assert.Single(rows, row => row.State == "Pending");
        await Process(settings, tenant); Assert.Equal(2, Provider.Calls.Count(call => call.Method == HttpMethod.Post));
        Assert.All(await States(settings), row => Assert.Equal("Verified", row.State));
    }

    [Fact]
    public async Task ChangedSourceBeforeWriteDefersObsoleteIntent()
    {
        SeedMenu(); var settings = Settings(); var tenant = new SourceClient(Snapshot()) { ChangeOnSecondRead = true };
        await Process(settings, tenant); Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Post);
        Assert.All(await States(settings), row => Assert.Equal("Pending", row.State));
    }

    [Fact]
    public async Task ReturningStockClearsSuspensionAndVerifiesAgain()
    {
        SeedMenu(); var settings = Settings(); await Process(settings);
        await Process(settings, new SourceClient(Snapshot(true) with { Revision = new string('b', 64) }));
        var write = Provider.Calls.Last(call => call.Method == HttpMethod.Post);
        Assert.Equal(JsonValueKind.Null, write.Body!.Value.GetProperty("suspension_info").GetProperty("suspension").ValueKind);
        Assert.All(await States(settings), row => { Assert.Equal("Verified", row.State); Assert.True(row.ObservedAvailable); });
    }

    private sealed class SourceClient(TenantAvailabilitySnapshot snapshot) : ITenantAvailabilityClient
    {
        public int Calls { get; private set; }
        public bool ChangeOnSecondRead { get; init; }
        public Task<TenantAvailabilitySnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ChangeOnSecondRead && Calls > 1 ? snapshot with { Revision = new string('c', 64) } : snapshot);
        }
    }
}
