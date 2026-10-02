using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CataloguePublicationRepositoryTests(GatewayFixture fixture)
{
    private static AvailabilityBinding Binding() => new(Guid.NewGuid().ToString(), Guid.NewGuid(), "tenant-fixture", "selection-v1");
    private static Task<CataloguePublication> Begin(PostgresCataloguePublications repository, AvailabilityBinding binding)
        => repository.Begin(binding, new string('a', 64), new string('b', 64), new string('c', 64),
            ProviderJson.Encode(new { items = new[] { new { id = "public-fixture" } } }), ProviderJson.Encode(new { items = Array.Empty<object>() }), default);

    [Fact]
    public async Task FirstTenantAnchorRejectsRebindingAcrossMappingRevisionsAndReadIsExactlyScoped()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var repository = new PostgresCataloguePublications(source);
        var binding = Binding(); var first = await Begin(repository, binding);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Begin(repository, binding with { TenantId = "foreign-tenant", CatalogueRevision = "selection-v2" }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Null(await repository.Latest(binding with { TenantId = "foreign-tenant" }, default));
        Assert.Null(await repository.Latest(binding with { ClientId = "foreign-client" }, default));
        Assert.Null(await repository.Latest(binding with { StoreId = Guid.NewGuid() }, default));
        Assert.Equal(first.Id, (await repository.Latest(binding, default))!.Id);
    }

    [Fact]
    public async Task OnlyLatestIntentCanBecomeVerifiedAndNewMappingCannotReuseOldEvidence()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var repository = new PostgresCataloguePublications(source);
        var binding = Binding(); var first = await Begin(repository, binding); var next = binding with { CatalogueRevision = "selection-v2" };
        var second = await Begin(repository, next);
        Assert.False(await repository.Verify(binding, first.Id, new string('d', 64), DateTimeOffset.UtcNow, default));
        Assert.Equal(second.Id, (await repository.Latest(binding, default))!.Id);
        Assert.False(await repository.Verify(binding, second.Id, new string('d', 64), DateTimeOffset.UtcNow, default));
        Assert.True(await repository.Verify(next, second.Id, new string('d', 64), DateTimeOffset.UtcNow, default));
        Assert.Equal("Verified", (await repository.Latest(next, default))!.State);
    }

    [Fact]
    public async Task RuntimeColumnGrantsPreventChangingPublishedBodiesWhileAllowingVerificationMetadata()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var repository = new PostgresCataloguePublications(source);
        var binding = Binding(); var row = await Begin(repository, binding); var role = "catalogue_runtime_" + Guid.NewGuid().ToString("N");
        await using (var grant = source.CreateCommand($"CREATE ROLE {role} NOLOGIN; GRANT SELECT, INSERT ON channel_catalogue_publications TO {role}; GRANT UPDATE(state, provider_hash, verified_at) ON channel_catalogue_publications TO {role};"))
            await grant.ExecuteNonQueryAsync();
        try
        {
            await using var connection = await source.OpenConnectionAsync();
            await using (var select = new NpgsqlCommand($"SET ROLE {role}; SELECT current_user", connection))
                Assert.Equal(role, await select.ExecuteScalarAsync());
            await using (var rewrite = new NpgsqlCommand("UPDATE channel_catalogue_publications SET menu = '{}'::jsonb WHERE id = $1", connection))
            {
                rewrite.Parameters.AddWithValue(row.Id);
                var error = await Assert.ThrowsAsync<PostgresException>(() => rewrite.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await using (var verify = new NpgsqlCommand("UPDATE channel_catalogue_publications SET state = 'Verified', provider_hash = repeat('d', 64), verified_at = now() WHERE id = $1", connection))
            {
                verify.Parameters.AddWithValue(row.Id); Assert.Equal(1, await verify.ExecuteNonQueryAsync());
            }
            await using (var reset = new NpgsqlCommand("RESET ROLE", connection)) await reset.ExecuteNonQueryAsync();
            Assert.Equal("Verified", (await repository.Latest(binding, default))!.State);
            Assert.Equal("public-fixture", (await repository.Latest(binding, default))!.Menu.GetProperty("items")[0].GetProperty("id").GetString());
        }
        finally
        {
            await using var remove = source.CreateCommand($"DROP OWNED BY {role}; DROP ROLE {role}");
            await remove.ExecuteNonQueryAsync();
        }
    }
}
