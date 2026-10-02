using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelAvailabilityOverrideTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task TimedPauseExpiresIntoCurrentTenantStockAndStatusDoesNotReusePausedConfirmation()
    {
        await InScope(async services =>
        {
            Provider.Menu = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "uber-sandbox-menu-readback.json")));
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var settings = Settings(); var clock = new Clock(DateTimeOffset.UtcNow);
            var binding = new AvailabilityBinding(GatewayFixture.ClientId, GatewayFixture.StoreId,
                settings.Store.TenantId, settings.Store.CatalogueRevision);
            var overrides = new PostgresChannelAvailabilityOverrides(database);
            await overrides.Set(binding, true, clock.GetUtcNow().AddMinutes(15), Guid.NewGuid(), clock.GetUtcNow(), default);
            var jobs = new PostgresChannelAvailabilityJobs(database);
            var webhook = Options.Create(new UberWebhookSettings { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] });
            var processor = new ChannelAvailabilityProcessor(Options.Create(settings), webhook, new Source(),
                new UberAvailabilityClient(Provider, services.GetRequiredService<ISandboxTokens>(), services.GetRequiredService<ISandboxMenu>(), clock),
                jobs, clock, overrides: overrides);
            var status = new ChannelAvailabilityStatus(Options.Create(settings), webhook, jobs, clock, overrides: overrides);
            await processor.Process(default);
            var paused = await status.Read(default);
            Assert.True(paused.GetProperty("paused").GetBoolean());
            Assert.All(paused.GetProperty("items").EnumerateArray(), item =>
            { Assert.False(item.GetProperty("observedAvailable").GetBoolean()); Assert.True(item.GetProperty("fresh").GetBoolean()); });
            Assert.Equal(2, Provider.Calls.Count(call => call.Method == HttpMethod.Post));
            clock.Now = clock.Now.AddMinutes(16);
            var expired = await status.Read(default);
            Assert.False(expired.GetProperty("paused").GetBoolean());
            Assert.All(expired.GetProperty("items").EnumerateArray(), item => Assert.False(item.GetProperty("fresh").GetBoolean()));
            await processor.Process(default);
            var resumed = await status.Read(default);
            Assert.True(resumed.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("itemId").GetString() == "sofra-test-meal-v1")
                .GetProperty("observedAvailable").GetBoolean());
            Assert.False(resumed.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("itemId").GetString() == "sofra-test-drink-v1")
                .GetProperty("observedAvailable").GetBoolean());
            Assert.All(resumed.GetProperty("items").EnumerateArray(), item => Assert.True(item.GetProperty("fresh").GetBoolean()));
            Assert.Equal(3, Provider.Calls.Count(call => call.Method == HttpMethod.Post));
            Assert.Null(await overrides.Read(binding with { TenantId = "other-tenant" }, default));
            Assert.Null(await overrides.Read(binding with { StoreId = Guid.NewGuid() }, default));
            Assert.Null(await overrides.Read(binding with { ClientId = "other-client" }, default));
            return true;
        });
    }

    [Fact]
    public async Task ChangedPauseIntentImmediatelyBeforeWriteDefersObsoleteAvailability()
    {
        await InScope(async services =>
        {
            Provider.Menu = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "uber-sandbox-menu-readback.json")));
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var clock = new Clock(DateTimeOffset.UtcNow); var settings = Settings();
            var overrides = new ChangingOverride(clock.Now);
            var jobs = new PostgresChannelAvailabilityJobs(database);
            var processor = new ChannelAvailabilityProcessor(Options.Create(settings), Options.Create(new UberWebhookSettings
            { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] }), new Source(),
                new UberAvailabilityClient(Provider, services.GetRequiredService<ISandboxTokens>(), services.GetRequiredService<ISandboxMenu>(), clock),
                jobs, clock, overrides: overrides);
            await processor.Process(default);
            Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Post);
            Assert.All(await jobs.Read(new(GatewayFixture.ClientId, GatewayFixture.StoreId, settings.Store.TenantId,
                settings.Store.CatalogueRevision), default), row => Assert.Equal("Pending", row.State));
            return true;
        });
    }

    private static TenantBridgeSettings Settings() => new()
    {
        Enabled = true,
        SyncAvailability = true,
        Store = new()
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = Guid.NewGuid().ToString(),
            CatalogueRevision = "override-fixture",
            CatalogueApiToken = "public-catalogue-fixture",
            Items = [new() { ProviderItemId = "sofra-test-meal-v1" }, new() { ProviderItemId = "sofra-test-drink-v1" }]
        }
    };
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Source : ITenantAvailabilityClient
    {
        public Task<TenantAvailabilitySnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAvailabilitySnapshot(new string('a', 64),
                [new("sofra-test-meal-v1", true, "Available"), new("sofra-test-drink-v1", false, "UnavailableProduct")]));
    }
    private sealed class ChangingOverride(DateTimeOffset now) : IChannelAvailabilityOverrides
    {
        private int _reads;
        public Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<ChannelAvailabilityOverride?>(new(++_reads == 1, null, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), now));
        public Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused, DateTimeOffset? pausedUntil,
            Guid actorId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
