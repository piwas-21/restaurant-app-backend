using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class DisconnectedChannelWorkerTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task PersistedDisconnectBlocksNewImportAndRecoveryUntilExactBindingReconnects()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var binding = new AvailabilityBinding(GatewayFixture.ClientId, GatewayFixture.StoreId,
                Guid.NewGuid().ToString(), "disconnect-fixture");
            var state = new PostgresChannelManagementConnectionState(database);
            Assert.False((await state.Read(binding, default)).IsDisconnected);
            await state.Set(binding, true, Guid.NewGuid(), DateTimeOffset.UtcNow, default);
            Assert.True((await state.Read(binding, default)).IsDisconnected);
            Assert.False((await state.Read(binding with { TenantId = "another-tenant" }, default)).IsDisconnected);
            Assert.False((await state.Read(binding with { StoreId = Guid.NewGuid() }, default)).IsDisconnected);
            Assert.False((await state.Read(binding with { ClientId = "another-client" }, default)).IsDisconnected);
            var settings = Options.Create(new TenantBridgeSettings
            {
                Enabled = true,
                UseTenantCatalogue = true,
                RecoverCreatedOrders = true,
                Store = new() { StoreId = binding.StoreId, TenantId = binding.TenantId, CatalogueRevision = binding.CatalogueRevision }
            });
            var webhook = Options.Create(new UberWebhookSettings { ClientId = binding.ClientId, StoreIds = [binding.StoreId] });
            var destination = new Destination();
            var imports = new TenantImportProcessor(settings, webhook, new PostgresChannelImportJobs(database),
                services.GetRequiredService<ISandboxOrders>(), new UberOrderNormalizer(), services.GetRequiredService<ISandboxCrypto>(),
                destination, TimeProvider.System, connectionState: state);
            Assert.False(await imports.Process(default));
            await new CreatedOrderRecovery(settings, webhook, services.GetRequiredService<ISandboxTokens>(), Provider,
                new PostgresCreatedOrderDiscoveries(database), TimeProvider.System, NullLogger<CreatedOrderRecovery>.Instance,
                connectionState: state).Process(default);
            Assert.False(destination.Called); Assert.Empty(Provider.Calls); Assert.Empty(Provider.Grants);
            await state.Set(binding, false, Guid.NewGuid(), DateTimeOffset.UtcNow, default);
            Assert.False((await state.Read(binding, default)).IsDisconnected);
            // An active channel reaches publication validation again; disconnect never erases that guard.
            await Assert.ThrowsAsync<ChannelConsoleException>(() => imports.Process(default));
            return true;
        });
    }
    private sealed class Destination : ITenantOrderClient
    {
        public bool Called { get; private set; }
        public Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest request, CancellationToken cancellationToken)
        { Called = true; throw new InvalidOperationException("A disconnected bridge must never forward an order."); }
    }
}
