using RestaurantSystem.Channels.Infrastructure;
using Npgsql;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelOrderLinksTests(GatewayFixture fixture)
{
    [Fact]
    public async Task ImportedLinkRequiresAllFiveIdentitiesAndImportedState()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var external = Guid.NewGuid(); var local = Guid.NewGuid();
        await using var seed = source.CreateCommand("""
            INSERT INTO channel_import_jobs(client_id,store_id,order_id,tenant_id,catalogue_revision,state,tenant_order_id)
            VALUES ($1,$2,$3,$4,'fixture-v1','Imported',$5)
            """);
        seed.Parameters.AddWithValue(GatewayFixture.ClientId); seed.Parameters.AddWithValue(store);
        seed.Parameters.AddWithValue(external); seed.Parameters.AddWithValue("links-fixture-tenant"); seed.Parameters.AddWithValue(local);
        Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        var links = new PostgresChannelOrderLinks(source);
        Assert.True(await links.IsImported(GatewayFixture.ClientId, store, "links-fixture-tenant", external, local, default));
        Assert.False(await links.IsImported(GatewayFixture.ClientId, Guid.NewGuid(), "links-fixture-tenant", external, local, default));
        Assert.False(await links.IsImported(GatewayFixture.ClientId, store, "another-tenant", external, local, default));
        Assert.False(await links.IsImported(GatewayFixture.ClientId, store, "links-fixture-tenant", Guid.NewGuid(), local, default));
        Assert.False(await links.IsImported(GatewayFixture.ClientId, store, "links-fixture-tenant", external, Guid.NewGuid(), default));
        Assert.False(await links.IsImported("another-client-fixture", store, "links-fixture-tenant", external, local, default));
        await using var quarantine = source.CreateCommand("UPDATE channel_import_jobs SET state='Quarantined' WHERE store_id=$1");
        quarantine.Parameters.AddWithValue(store); Assert.Equal(1, await quarantine.ExecuteNonQueryAsync());
        Assert.False(await links.IsImported(GatewayFixture.ClientId, store, "links-fixture-tenant", external, local, default));
    }
}
