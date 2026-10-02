using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelAvailabilityProcessor(IUberAvailabilityClient provider, IChannelAvailabilityJobs jobs,
    TimeProvider clock, IChannelAvailabilityPolicy policy) : IChannelAvailabilityProcessor
{
    public async Task Process(CancellationToken cancellationToken)
    {
        var plan = await policy.Resolve(cancellationToken);
        if (plan is null) return;
        await using var lease = await jobs.TryLease(plan.Binding, cancellationToken);
        if (lease is null) return;
        var desired = await policy.Desired(plan, cancellationToken);
        if (!await lease.Queue(desired.Revision, desired.Items.Select(item => new ChannelAvailabilityDesired(
            item.ProviderItemId, item.Available, item.Reason)).ToArray(), clock.GetUtcNow(), cancellationToken))
            throw new ChannelConsoleException(409, "Availability metadata belongs to a different tenant binding.");
        UberAvailabilitySnapshot actual;
        try { actual = await provider.Read(plan.Store, plan.ClientId, cancellationToken); }
        catch
        {
            foreach (var item in desired.Items)
                await lease.Observe(item.ProviderItemId, desired.Revision, "Uncertain", null, null, clock.GetUtcNow(), cancellationToken);
            throw;
        }
        await Reconcile(lease, plan, desired, actual, cancellationToken);
    }

    private async Task Reconcile(IChannelAvailabilityLease lease, ChannelAvailabilityPlan plan, TenantAvailabilitySnapshot desired,
        UberAvailabilitySnapshot actual, CancellationToken cancellationToken)
    {
        var writes = 0;
        foreach (var item in desired.Items)
        {
            if (actual.Items[item.ProviderItemId] == item.Available)
            {
                await Observe(lease, desired.Revision, item, actual, cancellationToken);
                continue;
            }
            if (writes >= plan.MaximumWrites)
            {
                await lease.Observe(item.ProviderItemId, desired.Revision, "Pending", actual.Items[item.ProviderItemId],
                    actual.Hash, clock.GetUtcNow(), cancellationToken);
                continue;
            }
            // Source changes during a bounded cycle must be reconciled before sending an obsolete intent.
            if ((await policy.Desired(plan, cancellationToken)).Revision != desired.Revision) return;
            // Persist uncertainty before the outbound write; a timeout/restart must never look verified.
            if (!await lease.Observe(item.ProviderItemId, desired.Revision, "Uncertain", actual.Items[item.ProviderItemId],
                actual.Hash, clock.GetUtcNow(), cancellationToken))
                throw new ChannelConsoleException(409, "Availability revision changed before dispatch.");
            writes++;
            await provider.Update(plan.Store, item, cancellationToken);
            actual = await provider.Read(plan.Store, plan.ClientId, cancellationToken);
            await Observe(lease, desired.Revision, item, actual, cancellationToken);
        }
    }

    private Task<bool> Observe(IChannelAvailabilityLease lease, string revision, TenantAvailabilityItem item,
        UberAvailabilitySnapshot actual, CancellationToken cancellationToken)
        => lease.Observe(item.ProviderItemId, revision, actual.Items[item.ProviderItemId] == item.Available ? "Verified" : "Mismatch",
            actual.Items[item.ProviderItemId], actual.Hash, clock.GetUtcNow(), cancellationToken);
}
