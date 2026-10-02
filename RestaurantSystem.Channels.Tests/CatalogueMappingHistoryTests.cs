using Npgsql;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CatalogueMappingHistoryTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task VerifiedSnapshotsSurvivePendingRemapAndAreIsolatedByClientStoreTenantAndRevision()
    {
        await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
        var publications = new PostgresCataloguePublications(database);
        var history = new PostgresCatalogueMappingHistory(database);
        var binding = new AvailabilityBinding(GatewayFixture.ClientId, GatewayFixture.StoreId,
            Guid.NewGuid().ToString(), "reviewed-original");
        var snapshot = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            catalogueRevision = "reviewed-original",
            items = new[]
            {
                new { providerItemId = "sofra-test-meal-v1",
                    productId = "11111111-1111-1111-1111-111111111111", variationId = (string?)null, variationName = (string?)null }
            }
        });
        var menu = System.Text.Json.JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() });
        var old = await publications.Begin(binding, new string('a', 64), new string('b', 64), new string('c', 64),
            menu, menu, default, snapshot);
        Assert.Null(await history.FindVerified(binding, binding.CatalogueRevision, default));
        Assert.True(await publications.Verify(binding, old.Id, new string('d', 64), DateTimeOffset.UtcNow, default));
        var pendingBinding = binding with { CatalogueRevision = "unconfirmed-replacement" };
        var pending = await publications.Begin(pendingBinding, new string('e', 64), new string('f', 64), new string('1', 64),
            menu, menu, default, snapshot);
        Assert.Equal(pending.Id, (await publications.Latest(binding, default))!.Id);
        Assert.Null(await history.FindVerified(binding, pendingBinding.CatalogueRevision, default));
        var retained = (await history.FindVerified(binding, binding.CatalogueRevision, default))!;
        Assert.Equal(old.Id, retained.Id);
        Assert.Equal("11111111-1111-1111-1111-111111111111", retained.MappingSnapshot!.Value
            .GetProperty("items")[0].GetProperty("productId").GetString());
        foreach (var wrong in new[] { binding with { ClientId = "other-client" },
            binding with { StoreId = Guid.NewGuid() }, binding with { TenantId = "other-tenant" } })
            Assert.Null(await history.FindVerified(wrong, binding.CatalogueRevision, default));
        Assert.False(await publications.Verify(binding, pending.Id, new string('2', 64), DateTimeOffset.UtcNow, default));
        Assert.Null(await history.FindVerified(binding, "missing-revision", default));
    }
}
