using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CreatedOrderRecoveryImportTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-store")]
    [InlineData("pre-enrollment-canonical")]
    [InlineData("denied-canonical")]
    [InlineData("missing-timezone-canonical")]
    [InlineData("future-canonical")]
    [InlineData("default-canonical")]
    public async Task RecoveredIdentityStillRequiresBoundCanonicalEvidenceBeforeTenantDelivery(string mode)
    {
        var id = Guid.NewGuid(); var placedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        var options = new TenantBridgeSettings
        {
            Enabled = true,
            RecoverCreatedOrders = true,
            EnrollmentStartedAt = placedAt.AddSeconds(-10),
            Store = new()
            {
                StoreId = GatewayFixture.StoreId,
                TenantId = Guid.NewGuid().ToString(),
                CatalogueRevision = "recovery-v1",
                Currency = "EUR",
                PublishedMenuHash = new string('a', 64),
                Items = [new() { ProviderItemId = "published-meal-v1", ProductId = Guid.NewGuid() }]
            },
        };
        Provider.CreatedOrders = ProviderJson.Encode(new
        {
            orders = new[] { new
        { id = id.ToString(), current_state = "CREATED", placed_at = placedAt.ToString("O") } }
        });
        var canonical = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/uber-order-simple.json")))!;
        canonical["id"] = id.ToString(); canonical["placed_at"] = placedAt.ToString("O");
        if (mode == "wrong-store") canonical["store"]!["id"] = Guid.NewGuid().ToString();
        if (mode == "pre-enrollment-canonical") canonical["placed_at"] = options.EnrollmentStartedAt.AddDays(-1).ToString("O");
        if (mode == "denied-canonical") canonical["current_state"] = "DENIED";
        if (mode == "missing-timezone-canonical") canonical["placed_at"] = placedAt.ToString("yyyy-MM-dd'T'HH:mm:ss");
        if (mode == "future-canonical") canonical["placed_at"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        if (mode == "default-canonical") canonical["placed_at"] = "0001-01-01T00:00:00Z";
        Provider.CanonicalOrder = JsonSerializer.SerializeToElement(canonical);
        await using var source = NpgsqlDataSource.Create(Database.ConnectionString);
        using var scope = Host.Services.CreateScope(); var services = scope.ServiceProvider;
        var webhook = new UberWebhookSettings { ClientId = GatewayFixture.ClientId, StoreIds = [GatewayFixture.StoreId] };
        var policy = ChannelProcessingPolicyTestSupport.OrderProcessing(options, webhook,
            ChannelProcessingPolicyTestSupport.Mapping(options.Store), ChannelProcessingPolicyTestSupport.Connected,
            ChannelProcessingPolicyTestSupport.Catalogue);
        await new CreatedOrderRecovery(services.GetRequiredService<ISandboxTokens>(), Provider,
            new PostgresCreatedOrderDiscoveries(source), TimeProvider.System,
            NullLogger<CreatedOrderRecovery>.Instance, policy).Process(default);
        var tenant = new RecordingTenant();
        var importer = new TenantImportProcessor(
            new PostgresChannelImportJobs(source), services.GetRequiredService<ISandboxOrders>(), new UberOrderNormalizer(),
            services.GetRequiredService<ISandboxCrypto>(), tenant, TimeProvider.System, policy);
        Assert.True(await importer.Process(default));
        if (mode == "valid")
        {
            var request = Assert.Single(tenant.Requests); Assert.Equal(id.ToString(), request.ExternalOrderId);
            Assert.Equal(5, request.MerchantTotal); Assert.Null(request.ReportedTax);
            Assert.Equal("No peanuts; severe allergy.\nKeep this instruction.", Assert.Single(request.Items).Instructions);
            await using var query = source.CreateCommand("SELECT state, encrypted_request FROM channel_import_jobs WHERE order_id = $1");
            query.Parameters.AddWithValue(id); await using var row = await query.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
            Assert.Equal("Imported", row.GetString(0)); Assert.True(row.IsDBNull(1));
        }
        else Assert.Empty(tenant.Requests);
        Assert.All(Provider.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        await using var receipts = source.CreateCommand("SELECT count(*) FROM channel_webhook_receipts WHERE resource_id = $1");
        receipts.Parameters.AddWithValue(id.ToString()); Assert.Equal(0L, await receipts.ExecuteScalarAsync());
    }

    private sealed class RecordingTenant : ITenantOrderClient
    {
        public List<TenantOrderRequest> Requests { get; } = [];
        public Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest order, CancellationToken cancellationToken)
        {
            Requests.Add(order); return Task.FromResult(new TenantImportResult(Guid.NewGuid(), false));
        }
    }
}
