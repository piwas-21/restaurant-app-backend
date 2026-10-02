using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CreatedOrderRecoveryTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    private TenantBridgeSettings Settings() => new()
    {
        Enabled = true,
        RecoverCreatedOrders = true,
        EnrollmentStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        Store = new() { StoreId = GatewayFixture.StoreId, TenantId = Guid.NewGuid().ToString(), CatalogueRevision = "recovery-v1" },
    };

    private Task<bool> Process(TenantBridgeSettings settings) => InScope(async services =>
    {
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        var webhook = new UberWebhookSettings { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] };
        var policy = ChannelProcessingPolicyTestSupport.OrderProcessing(settings, webhook,
            ChannelProcessingPolicyTestSupport.Mapping(settings.Store), ChannelProcessingPolicyTestSupport.Connected,
            ChannelProcessingPolicyTestSupport.Catalogue);
        await new CreatedOrderRecovery(services.GetRequiredService<ISandboxTokens>(), Provider,
            new PostgresCreatedOrderDiscoveries(source), TimeProvider.System,
            NullLogger<CreatedOrderRecovery>.Instance, policy).Process(default);
        return true;
    });

    private static object Row(Guid id, DateTimeOffset time, string state = "CREATED")
        => new { id = id.ToString("D"), current_state = state, placed_at = time.ToString("O", CultureInfo.InvariantCulture) };

    [Fact]
    public async Task MissedWebhookRecoversDurableIdentityAndLaterSignedNotificationCannotDuplicateOrRebind()
    {
        var settings = Settings(); var order = Guid.NewGuid();
        Provider.CreatedOrders = ProviderJson.Encode(new { orders = new[] { Row(order, DateTimeOffset.UtcNow.AddMinutes(-1)) } });
        await Process(settings); await Process(settings);
        Assert.Single(Provider.Grants, grant => grant["scope"] == "eats.store.orders.read");
        Assert.All(Provider.Calls, call => Assert.Equal($"/v1/eats/stores/{GatewayFixture.StoreId:D}/created-orders?limit=200", call.Path));
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        var repository = new PostgresConsoleRepository(source);
        Assert.True(await repository.HasOrder(GatewayFixture.ClientId, GatewayFixture.StoreId, order.ToString(), default));
        Assert.False(await repository.HasOrder("foreign-client", GatewayFixture.StoreId, order.ToString(), default));
        Assert.False(await repository.HasOrder(GatewayFixture.ClientId, Guid.NewGuid(), order.ToString(), default));
        await new PostgresWebhookInbox(source).Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification",
            GatewayFixture.StoreId, order.ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);
        var jobs = new PostgresChannelImportJobs(source);
        await jobs.Discover(GatewayFixture.ClientId, GatewayFixture.StoreId, "foreign-tenant", "other-revision", settings.EnrollmentStartedAt, default);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, GatewayFixture.StoreId, "foreign-tenant", default));
        var claim = await jobs.Claim(GatewayFixture.ClientId, GatewayFixture.StoreId, settings.Store.TenantId, default);
        Assert.NotNull(claim); Assert.Equal(order, claim.OrderId); Assert.Equal("recovery-v1", claim.CatalogueRevision);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, GatewayFixture.StoreId, settings.Store.TenantId, default));
        await using var evidence = source.CreateCommand("SELECT discovery_source, discovery_hash, encrypted_request FROM channel_import_jobs WHERE order_id = $1");
        evidence.Parameters.AddWithValue(order); await using var reader = await evidence.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal("provider_poll", reader.GetString(0)); Assert.Matches("^[a-f0-9]{64}$", reader.GetString(1)); Assert.True(reader.IsDBNull(2));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("not-opted-in")]
    [InlineData("wrong-store")]
    public async Task DisabledPausedUnapprovedOrUnconfiguredRecoveryCannotFetch(string mode)
    {
        var settings = Settings();
        if (mode == "disabled") settings.Enabled = false;
        if (mode == "paused") settings.Paused = true;
        if (mode == "not-opted-in") settings.RecoverCreatedOrders = false;
        if (mode == "wrong-store")
        {
            settings.Store.StoreId = Guid.NewGuid();
            await Assert.ThrowsAsync<ChannelConsoleException>(() => Process(settings));
        }
        else await Process(settings);
        Assert.Empty(Provider.Grants); Assert.Empty(Provider.Calls);
    }

    [Theory]
    [InlineData("old")]
    [InlineData("invalid-id")]
    [InlineData("wrong-state")]
    [InlineData("future")]
    [InlineData("missing-timezone")]
    [InlineData("wrong-client")]
    [InlineData("duplicate-conflict")]
    [InlineData("malformed-root")]
    [InlineData("provider-error")]
    [InlineData("over-limit")]
    public async Task OldOrHostileListDoesNotPartiallyCreateAnEligibleJob(string mode)
    {
        var settings = Settings(); var id = Guid.NewGuid(); var good = Row(id, DateTimeOffset.UtcNow.AddSeconds(-10));
        object bad = mode switch
        {
            "old" => Row(id, settings.EnrollmentStartedAt.AddMinutes(-1)),
            "wrong-state" => Row(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-10), "ACCEPTED"),
            "future" => Row(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            "missing-timezone" => new { id = Guid.NewGuid().ToString(), current_state = "CREATED", placed_at = "2026-10-02T00:00:00" },
            "invalid-id" => new { id = "not-an-order", current_state = "CREATED", placed_at = DateTimeOffset.UtcNow.ToString("O") },
            _ => good,
        };
        Provider.CreatedOrders = ProviderJson.Encode(new { orders = mode == "old" ? new[] { bad } : new[] { good, bad } });
        if (mode == "wrong-client") Provider.CreatedOrdersClientId = "foreign-client";
        if (mode == "provider-error") Provider.CreatedOrdersStatus = 429;
        if (mode == "malformed-root") Provider.CreatedOrders = ProviderJson.Encode(new[] { good });
        if (mode == "duplicate-conflict") Provider.CreatedOrders = ProviderJson.Encode(new { orders = new[] { good, Row(id, DateTimeOffset.UtcNow.AddMinutes(-1)) } });
        if (mode == "over-limit") settings.RecoveryListLimit = 1;
        if (mode == "old") await Process(settings);
        else await Assert.ThrowsAsync<ChannelConsoleException>(() => Process(settings));
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        Assert.Null(await new PostgresChannelImportJobs(source).Claim(GatewayFixture.ClientId, GatewayFixture.StoreId, settings.Store.TenantId, default));
    }

    [Fact]
    public async Task SaturatedListStillRecoversCandidatesWithoutDiscardingTheWholeBatch()
    {
        var settings = Settings(); settings.RecoveryListLimit = 1; var order = Guid.NewGuid();
        Provider.CreatedOrders = ProviderJson.Encode(new { orders = new[] { Row(order, DateTimeOffset.UtcNow.AddSeconds(-10)) } });
        await Process(settings);
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        Assert.Equal(order, (await new PostgresChannelImportJobs(source).Claim(GatewayFixture.ClientId,
            GatewayFixture.StoreId, settings.Store.TenantId, default))!.OrderId);
    }
}
