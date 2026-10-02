using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantImportProcessorTests(GatewayFixture fixture)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static TenantBridgeSettings Settings(Guid store) => new()
    {
        Enabled = true,
        EnrollmentStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        Store = new()
        {
            StoreId = store,
            TenantId = "processor-test-tenant",
            BaseUrl = "https://tenant.example/",
            ApiToken = "public-fixture-only",
            Currency = "EUR",
            CatalogueRevision = "published-v1",
            PublishedMenuHash = new string('a', 64),
            Items = [new() { ProviderItemId = "published-meal-v1", ProductId = Guid.NewGuid() }]
        },
    };

    private static SandboxCrypto Crypto() => new(Options.Create(new SandboxConsoleSettings { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
    private static TenantImportProcessor Processor(TenantBridgeSettings settings, IChannelImportJobs jobs,
        ISandboxOrders orders, ITenantOrderClient tenant)
        => new(Options.Create(settings), Options.Create(new UberWebhookSettings { ClientId = GatewayFixture.ClientId }),
            jobs, orders, new UberOrderNormalizer(), Crypto(), tenant, TimeProvider.System);

    private static JsonNode Canonical(Guid store, Guid order)
    {
        var body = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/uber-order-simple.json")))!;
        body["id"] = order.ToString(); body["store"]!["id"] = store.ToString(); return body;
    }

    private static async Task Seed(NpgsqlDataSource source, Guid store, Guid order)
        => await new PostgresWebhookInbox(source).Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(),
            "orders.notification", store, order.ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);

    [Fact]
    public async Task LostTenantResponseRetriesCommittedOriginalWithoutFetchingChangedProviderEvidence()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); await Seed(source, store, order);
        var settings = Settings(store); var jobs = new PostgresChannelImportJobs(source);
        var provider = new OrderReader(Canonical(store, order));
        var destination = new RecordingTenant(async call =>
        {
            await using var check = source.CreateCommand("SELECT state, encrypted_request, request_hash FROM channel_import_jobs WHERE store_id = $1");
            check.Parameters.AddWithValue(store);
            await using var row = await check.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
            Assert.Equal("Prepared", row.GetString(0)); Assert.False(row.IsDBNull(1));
            Assert.DoesNotContain("No peanuts", row.GetString(1), StringComparison.Ordinal);
            Assert.Matches("^[a-f0-9]{64}$", row.GetString(2));
            if (call == 1) throw new ChannelConsoleException(502, "Public fixture lost response after commit.");
        });
        Assert.True(await Processor(settings, jobs, provider, destination).Process(default));
        Assert.Equal(1, provider.Reads); Assert.Single(destination.Requests);
        // Provider state and prices have changed, but a lost tenant response must retry the frozen original wire.
        provider.Body["current_state"] = "DENIED"; provider.Body["payment"]!["charges"]!["total"]!["amount"] = 999;
        await using (var ready = source.CreateCommand("UPDATE channel_import_jobs SET available_at = now() WHERE store_id = $1"))
        {
            ready.Parameters.AddWithValue(store); Assert.Equal(1, await ready.ExecuteNonQueryAsync());
        }
        var restarted = new PostgresChannelImportJobs(source);
        Assert.True(await Processor(settings, restarted, provider, destination).Process(default));
        Assert.Equal(1, provider.Reads); Assert.Equal(2, destination.Requests.Count);
        Assert.Equal(destination.Requests[0], destination.Requests[1]);
        using var original = JsonDocument.Parse(destination.Requests[1]);
        Assert.Equal(5m, original.RootElement.GetProperty("merchantTotal").GetDecimal());
        Assert.Equal("No peanuts; severe allergy.\nKeep this instruction.", original.RootElement.GetProperty("items")[0].GetProperty("instructions").GetString());
        await using var final = source.CreateCommand("SELECT state, tenant_order_id, encrypted_request FROM channel_import_jobs WHERE store_id = $1");
        final.Parameters.AddWithValue(store); await using var result = await final.ExecuteReaderAsync(); Assert.True(await result.ReadAsync());
        Assert.Equal("Imported", result.GetString(0)); Assert.Equal(destination.OrderId, result.GetGuid(1)); Assert.True(result.IsDBNull(2));
    }

    [Fact]
    public async Task PreparedWireFromBeforeContactCodeUpgradeSurvivesLostResponseAndRestart()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); await Seed(source, store, order);
        var settings = Settings(store); var jobs = new PostgresChannelImportJobs(source);
        // Manually authored old contract, independent of the current serializer and optional field.
        var original = $$"""
            {"provider":"uber-eats","storeId":"{{store:D}}","externalOrderId":"{{order:D}}","displayId":"OLD","canonicalOrderHash":"{{new string('a', 64)}}","currency":"EUR","merchantTotal":5,"reportedTax":null,"placedAt":"2026-10-01T00:00:00+00:00","fulfillmentType":"DELIVERY_BY_UBER","customerName":null,"customerPhone":null,"instructions":null,"items":[{"productId":"{{settings.Store.Items[0].ProductId:D}}","variationId":null,"name":"Old meal","variationName":null,"quantity":1,"unitPrice":5,"total":5,"instructions":null}]}
            """;
        await jobs.Discover(GatewayFixture.ClientId, store, settings.Store.TenantId, "published-v1", settings.EnrollmentStartedAt, default);
        var claim = (await jobs.Claim(GatewayFixture.ClientId, store, settings.Store.TenantId, default))!;
        var purpose = $"tenant-import:{claim.ClientId}:{claim.StoreId:D}:{claim.OrderId:D}:{claim.TenantId}:{claim.CatalogueRevision}";
        Assert.True(await jobs.Prepare(claim, Crypto().Protect(original, purpose), Crypto().Hash(original), DateTimeOffset.UtcNow.AddDays(1), default));
        await jobs.Defer(claim, "DeliveryUncertain", DateTimeOffset.UtcNow, false, default);
        var provider = new OrderReader(Canonical(store, order));
        var destination = new RecordingTenant(call => call == 1
            ? Task.FromException(new ChannelConsoleException(502, "Public fixture lost reply.")) : Task.CompletedTask);
        Assert.True(await Processor(settings, jobs, provider, destination).Process(default));
        await using (var ready = source.CreateCommand("UPDATE channel_import_jobs SET available_at = now() WHERE store_id = $1"))
        {
            ready.Parameters.AddWithValue(store); Assert.Equal(1, await ready.ExecuteNonQueryAsync());
        }
        Assert.True(await Processor(settings, new PostgresChannelImportJobs(source), provider, destination).Process(default));
        Assert.Equal(0, provider.Reads); Assert.Equal(2, destination.Requests.Count);
        Assert.All(destination.Requests, request => Assert.Equal(original, request));
    }

    [Fact]
    public async Task ExpiredLostResponseRemainsUnconfirmedEvenWhenTenantCommittedTheOrder()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); await Seed(source, store, order);
        var settings = Settings(store); var jobs = new PostgresChannelImportJobs(source);
        var committedOrders = new HashSet<Guid>();
        var destination = new RecordingTenant(_ =>
        {
            committedOrders.Add(order);
            return Task.FromException(new ChannelConsoleException(502, "Public fixture lost response after commit."));
        });
        Assert.True(await Processor(settings, jobs, new OrderReader(Canonical(store, order)), destination).Process(default));
        Assert.Contains(order, committedOrders); Assert.Single(destination.Requests);
        await using (var expire = source.CreateCommand("UPDATE channel_import_jobs SET payload_expires_at = now() - interval '1 second' WHERE store_id = $1 AND order_id = $2"))
        {
            expire.Parameters.AddWithValue(store); expire.Parameters.AddWithValue(order);
            Assert.Equal(1, await expire.ExecuteNonQueryAsync());
        }
        await jobs.ExpirePayloads(default);
        var view = new ChannelImportView(Options.Create(settings), Options.Create(new UberWebhookSettings
        {
            ClientId = GatewayFixture.ClientId,
            StoreIds = [store]
        }), new PostgresChannelImportStatus(source), TimeProvider.System);
        var body = await view.Read("", default); var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(order, row.GetProperty("orderId").GetGuid());
        Assert.Equal("Quarantined", row.GetProperty("state").GetString());
        Assert.Equal("PayloadExpired", row.GetProperty("code").GetString());
        Assert.True(row.GetProperty("reviewRequired").GetBoolean());
        Assert.False(row.GetProperty("deliveryConfirmed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("tenantOrderId").ValueKind);
        Assert.Contains(order, committedOrders);
    }

    [Fact]
    public async Task UnsupportedCanonicalItemIsQuarantinedWithoutTenantCall()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); await Seed(source, store, order);
        var canonical = Canonical(store, order); canonical["cart"]!["items"]![0] = null;
        var destination = new RecordingTenant(_ => Task.CompletedTask); var jobs = new PostgresChannelImportJobs(source);
        Assert.True(await Processor(Settings(store), jobs, new OrderReader(canonical), destination).Process(default));
        Assert.Empty(destination.Requests);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, store, "processor-test-tenant", default));
        await using var check = source.CreateCommand("SELECT state, last_code, encrypted_request FROM channel_import_jobs WHERE store_id = $1");
        check.Parameters.AddWithValue(store); await using var row = await check.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
        Assert.Equal("Quarantined", row.GetString(0)); Assert.Equal("ContractRejected", row.GetString(1)); Assert.True(row.IsDBNull(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausedOrDisabledForwardingStillErasesExpiredPayload(bool disabled)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); await Seed(source, store, order);
        var settings = Settings(store); var jobs = new PostgresChannelImportJobs(source);
        await jobs.Discover(GatewayFixture.ClientId, store, settings.Store.TenantId, "published-v1", settings.EnrollmentStartedAt, default);
        var claim = (await jobs.Claim(GatewayFixture.ClientId, store, settings.Store.TenantId, default))!;
        Assert.True(await jobs.Prepare(claim, "public-expired-ciphertext-fixture", new string('a', 64), DateTimeOffset.UtcNow.AddSeconds(-1), default));
        settings.Enabled = !disabled; settings.Paused = !disabled;
        var provider = new OrderReader(Canonical(store, order)); var destination = new RecordingTenant(_ => Task.CompletedTask);
        Assert.False(await Processor(settings, jobs, provider, destination).Process(default));
        Assert.Equal(0, provider.Reads); Assert.Empty(destination.Requests);
        await using var check = source.CreateCommand("SELECT state, encrypted_request, last_code FROM channel_import_jobs WHERE store_id = $1");
        check.Parameters.AddWithValue(store); await using var row = await check.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
        Assert.Equal("Quarantined", row.GetString(0)); Assert.True(row.IsDBNull(1)); Assert.Equal("PayloadExpired", row.GetString(2));
    }

    private sealed class OrderReader(JsonNode body) : ISandboxOrders
    {
        public JsonNode Body { get; } = body;
        public int Reads { get; private set; }
        public Task<JsonElement> Read(string orderId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(JsonSerializer.SerializeToElement(Body)); }
        public Task<JsonElement> Receipts(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingTenant(Func<int, Task> action) : ITenantOrderClient
    {
        public List<string> Requests { get; } = [];
        public Guid OrderId { get; } = Guid.NewGuid();
        public async Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest order, CancellationToken cancellationToken)
        {
            Requests.Add(JsonSerializer.Serialize(order, Wire)); await action(Requests.Count);
            return new(OrderId, Requests.Count > 1);
        }
    }
}
