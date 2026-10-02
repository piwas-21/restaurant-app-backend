using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantChannelAvailabilityReconcilerTests(GatewayFixture fixture)
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly string RevisionA = new('a', 64);
    private static readonly string RevisionB = new('b', 64);
    private static readonly string ProviderHash = new('c', 64);

    [Theory]
    [InlineData("source")]
    [InlineData("override")]
    public async Task SourceOrPauseChangeBeforeLeaseGrantCannotVerifyStaleAvailability(string change)
    {
        await using var harness = await Harness.Create(fixture);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Jobs.BeforeLease = async token =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(token);
        };

        var pending = harness.Reconciler.Reconcile(harness.State, ActorId, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (change == "source") harness.Source.Set(RevisionB);
            else await harness.Overrides.Set(harness.Binding, true, null, ActorId, DateTimeOffset.UtcNow, default);
        }
        finally { release.TrySetResult(true); }

        var result = await pending;
        Assert.Equal("open", result.GetProperty("status").GetString());
        Assert.Equal("AvailabilityIntentChanged", result.GetProperty("code").GetString());
        Assert.Equal(0, harness.Provider.ReadCount);
        var persisted = Assert.Single(await harness.Repository.Read(harness.Binding, default));
        Assert.Equal("Pending", persisted.State);
        Assert.Null(persisted.ObservedAvailable);
    }

    [Fact]
    public async Task SourceChangeDuringProviderReadCannotVerifyStaleAvailability()
    {
        await using var harness = await Harness.Create(fixture);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Provider.BeforeRead = async token =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(token);
        };

        var pending = harness.Reconciler.Reconcile(harness.State, ActorId, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            harness.Source.Set(RevisionB);
        }
        finally { release.TrySetResult(true); }

        var result = await pending;
        Assert.Equal("open", result.GetProperty("status").GetString());
        Assert.Equal("AvailabilityIntentChanged", result.GetProperty("code").GetString());
        Assert.Equal(1, harness.Provider.ReadCount);
        var persisted = Assert.Single(await harness.Repository.Read(harness.Binding, default));
        Assert.Equal("Pending", persisted.State);
        Assert.Null(persisted.ObservedAvailable);
        Assert.DoesNotContain(harness.Audit.Results, code => code == "Verified");
    }

    [Fact]
    public async Task CurrentRevisionWithSuccessfulObserveResolvesException()
    {
        await using var harness = await Harness.Create(fixture);

        var result = await harness.Reconciler.Reconcile(harness.State, ActorId, default);

        Assert.Equal("resolved", result.GetProperty("status").GetString());
        var persisted = Assert.Single(await harness.Repository.Read(harness.Binding, default));
        Assert.Equal("Verified", persisted.State);
        Assert.True(persisted.ObservedAvailable);
        Assert.Equal(ProviderHash, persisted.ProviderHash);
        Assert.Contains("Verified", harness.Audit.Results);
    }

    [Fact]
    public async Task FailedObserveForSupersededStateNeverReturnsResolved()
    {
        await using var harness = await Harness.Create(fixture);
        await using (var lease = await harness.Repository.TryLease(harness.Binding, default))
            Assert.True(await lease!.Queue(RevisionB, [new("meal", true, "Available")], DateTimeOffset.UtcNow, default));

        var result = await harness.Reconciler.Reconcile(harness.State, ActorId, default);

        Assert.Equal("open", result.GetProperty("status").GetString());
        Assert.Equal("AvailabilityIntentChanged", result.GetProperty("code").GetString());
        var persisted = Assert.Single(await harness.Repository.Read(harness.Binding, default));
        Assert.Equal(RevisionB, persisted.SourceRevision);
        Assert.Equal("Pending", persisted.State);
        Assert.DoesNotContain(harness.Audit.Results, code => code == "Verified");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(NpgsqlDataSource dataSource, TenantStoreBinding store, AvailabilityBinding binding,
            PostgresChannelAvailabilityJobs repository, LeaseGateJobs jobs, PostgresChannelAvailabilityOverrides overrides,
            TenantChannelAvailabilityReconciler reconciler, MutableSource source, Provider provider, Audit audit,
            ChannelAvailabilityState state)
        {
            DataSource = dataSource; Store = store; Binding = binding; Repository = repository; Jobs = jobs;
            Overrides = overrides; Reconciler = reconciler; Source = source; Provider = provider; Audit = audit; State = state;
        }

        public NpgsqlDataSource DataSource { get; }
        public TenantStoreBinding Store { get; }
        public AvailabilityBinding Binding { get; }
        public PostgresChannelAvailabilityJobs Repository { get; }
        public LeaseGateJobs Jobs { get; }
        public PostgresChannelAvailabilityOverrides Overrides { get; }
        public TenantChannelAvailabilityReconciler Reconciler { get; }
        public MutableSource Source { get; }
        public Provider Provider { get; }
        public Audit Audit { get; }
        public ChannelAvailabilityState State { get; }

        public static async Task<Harness> Create(GatewayFixture fixture)
        {
            var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
            var clientId = Guid.NewGuid().ToString("N");
            var store = new TenantStoreBinding
            {
                StoreId = GatewayFixture.StoreId,
                TenantId = "availability-reconcile-tenant",
                BaseUrl = "https://tenant.example/",
                ApiToken = "orders",
                CatalogueApiToken = "catalogue",
                Currency = "EUR",
                CatalogueRevision = "catalogue-v1",
                PublishedMenuHash = ProviderHash,
                Items = [new() { ProviderItemId = "meal", ProductId = Guid.NewGuid() }]
            };
            var binding = new AvailabilityBinding(clientId, store.StoreId, store.TenantId, store.CatalogueRevision);
            var repository = new PostgresChannelAvailabilityJobs(dataSource);
            var jobs = new LeaseGateJobs(repository);
            var overrides = new PostgresChannelAvailabilityOverrides(dataSource);
            var resolver = new Resolver(store);
            var state = new TenantChannelAvailabilityState(resolver, jobs, overrides);
            var source = new MutableSource(RevisionA);
            var provider = new Provider();
            var audit = new Audit();
            var context = new TenantManagementContext(
                Options.Create(new TenantBridgeSettings { Enabled = true, UseTenantCatalogue = true, SyncAvailability = true, Store = store }),
                Options.Create(new TenantManagementGatewaySettings { Enabled = true }),
                Options.Create(new UberWebhookSettings { ClientId = clientId, StoreIds = [store.StoreId] }),
                TimeProvider.System);
            var reconciler = new TenantChannelAvailabilityReconciler(context, state, source, provider, audit);
            await using (var lease = await repository.TryLease(binding, default))
                Assert.True(await lease!.Queue(RevisionA, [new("meal", true, "Available")], DateTimeOffset.UtcNow, default));
            var row = Assert.Single(await repository.Read(binding, default));
            return new(dataSource, store, binding, repository, jobs, overrides, reconciler, source, provider, audit, row);
        }

        public ValueTask DisposeAsync() => DataSource.DisposeAsync();
    }

    private sealed class Resolver(TenantStoreBinding store) : ICatalogueMappingResolver
    {
        public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => Task.FromResult(store);
        public Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken)
            => Task.FromResult(store);
    }

    private sealed class LeaseGateJobs(IChannelAvailabilityJobs inner) : IChannelAvailabilityJobs
    {
        public Func<CancellationToken, Task>? BeforeLease { get; set; }
        public Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => inner.Read(binding, cancellationToken);
        public async Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
        {
            if (BeforeLease is not null) await BeforeLease(cancellationToken);
            return await inner.TryLease(binding, cancellationToken);
        }
    }

    private sealed class MutableSource(string revision) : ITenantAvailabilityClient
    {
        private TenantAvailabilitySnapshot _snapshot = Snapshot(revision);
        public Task<TenantAvailabilitySnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
            => Task.FromResult(Volatile.Read(ref _snapshot));
        public void Set(string nextRevision) => Volatile.Write(ref _snapshot, Snapshot(nextRevision));
        private static TenantAvailabilitySnapshot Snapshot(string revision)
            => new(revision, [new("meal", true, "Available")]);
    }

    private sealed class Provider : IUberAvailabilityClient
    {
        private int _readCount;
        public Func<CancellationToken, Task>? BeforeRead { get; set; }
        public int ReadCount => Volatile.Read(ref _readCount);
        public async Task<UberAvailabilitySnapshot> Read(TenantStoreBinding store, string clientId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            if (BeforeRead is not null) await BeforeRead(cancellationToken);
            return new(ProviderHash, new Dictionary<string, bool>(StringComparer.Ordinal) { ["meal"] = true });
        }
        public Task Update(TenantStoreBinding store, TenantAvailabilityItem item, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class Audit : IChannelManagementAudit
    {
        public List<string> Results { get; } = [];
        public Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode,
            Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        { Results.Add(resultCode); return Task.CompletedTask; }
        public Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding, long? beforeSequence,
            int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChannelManagementAuditRecord>>([]);
    }
}
