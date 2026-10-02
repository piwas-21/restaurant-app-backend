using Npgsql;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelAvailabilityJobsTests(GatewayFixture fixture)
{
    private static AvailabilityBinding Binding() => new(Guid.NewGuid().ToString(), GatewayFixture.StoreId, "tenant-a", "published-v1");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly string Revision = new('a', 64);
    private static readonly string Hash = new('b', 64);
    private static readonly ChannelAvailabilityDesired[] Items = [new("meal", false, "UnavailableProduct"), new("drink", true, "Available")];

    [Fact]
    public async Task StoreLeaseExcludesOtherRevisionsAndTenantsAndReleasesAfterDisposal()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using (var first = await repository.TryLease(binding, default))
        {
            Assert.NotNull(first);
            Assert.Null(await repository.TryLease(binding with { TenantId = "foreign", CatalogueRevision = "other" }, default));
            await using var other = await repository.TryLease(binding with { StoreId = Guid.NewGuid() }, default);
            Assert.NotNull(other);
        }
        await using var next = await repository.TryLease(binding, default); Assert.NotNull(next);
    }

    [Fact]
    public async Task VerifiedEvidenceSurvivesRestartUnchangedQueueButNewRevisionBecomesPending()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using (var lease = await repository.TryLease(binding, default))
        {
            Assert.True(await lease!.Queue(Revision, Items, Now, default));
            Assert.True(await lease.Observe("meal", Revision, "Verified", false, Hash, Now, default));
        }
        var restarted = new PostgresChannelAvailabilityJobs(source);
        await using var next = await restarted.TryLease(binding, default);
        Assert.True(await next!.Queue(Revision, Items, Now.AddSeconds(30), default));
        var meal = (await restarted.Read(binding, default)).Single(row => row.ProviderItemId == "meal");
        Assert.Equal("Verified", meal.State); Assert.Equal(Now, meal.VerifiedAt); Assert.Equal(Hash, meal.ProviderHash);
        Assert.True(await next.Queue(new string('c', 64), Items, Now.AddMinutes(1), default));
        meal = (await restarted.Read(binding, default)).Single(row => row.ProviderItemId == "meal");
        Assert.Equal("Pending", meal.State); Assert.Null(meal.VerifiedAt);
        Assert.False(await next.Observe("meal", Revision, "Verified", false, Hash, Now.AddMinutes(2), default));
        Assert.Empty(await restarted.Read(binding with { TenantId = "foreign" }, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RebindingRefusalRollsBackWholeBatchAndPreservesFirstTenant(bool changedRevision)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using (var first = await repository.TryLease(binding, default)) Assert.True(await first!.Queue(Revision, Items, Now, default));
        var foreign = binding with { TenantId = "foreign", CatalogueRevision = changedRevision ? "new-revision" : binding.CatalogueRevision };
        await using var lease = await repository.TryLease(foreign, default);
        Assert.False(await lease!.Queue(Revision, [new("new-item", true, "Available"), Items[0]], Now, default));
        Assert.Empty(await repository.Read(foreign, default)); Assert.Equal(2, (await repository.Read(binding, default)).Count);
    }

    [Theory]
    [InlineData("hash-null")]
    [InlineData("observed-null")]
    [InlineData("observed-mismatch")]
    [InlineData("hash-invalid")]
    public async Task DatabaseRejectsVerifiedWithoutMatchingIndependentEvidence(string mutation)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using var lease = await repository.TryLease(binding, default);
        Assert.True(await lease!.Queue(Revision, Items, Now, default));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => lease.Observe("meal", Revision, "Verified",
            mutation == "observed-null" ? null : mutation == "observed-mismatch", mutation == "hash-null" ? null
                : mutation == "hash-invalid" ? new string('z', 64) : Hash, Now, default));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        var meal = (await repository.Read(binding, default)).Single(row => row.ProviderItemId == "meal");
        Assert.Equal("Pending", meal.State); Assert.Null(meal.VerifiedAt);
        Assert.True(await lease.Observe("meal", Revision, "Verified", false, Hash, Now, default));
    }

    [Fact]
    public async Task InvalidRevisionCannotPartiallyPersistBatch()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using var lease = await repository.TryLease(binding, default);
        await Assert.ThrowsAsync<PostgresException>(() => lease!.Queue(new string('z', 64), Items, Now, default));
        Assert.Empty(await repository.Read(binding, default));
    }

    [Fact]
    public async Task SameTenantCanAdvanceRevisionButDatabaseCannotInsertForeignTenantEvidence()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var repository = new PostgresChannelAvailabilityJobs(source); var binding = Binding();
        await using (var lease = await repository.TryLease(binding, default)) Assert.True(await lease!.Queue(Revision, Items, Now, default));
        var next = binding with { CatalogueRevision = "published-v2" };
        await using (var lease = await repository.TryLease(next, default)) Assert.True(await lease!.Queue(Revision, Items, Now, default));
        Assert.Equal(2, (await repository.Read(next, default)).Count);
        await using var command = source.CreateCommand("""
            INSERT INTO channel_availability_states (client_id, store_id, tenant_id, catalogue_revision,
                provider_item_id, source_revision, desired_available, source_reason, updated_at)
            SELECT client_id, store_id, 'foreign', 'foreign-revision', provider_item_id,
                source_revision, desired_available, source_reason, updated_at
            FROM channel_availability_states WHERE client_id = $1 AND store_id = $2 AND catalogue_revision = 'published-v1'
            """);
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal(2, (await repository.Read(binding, default)).Count);
    }
}
