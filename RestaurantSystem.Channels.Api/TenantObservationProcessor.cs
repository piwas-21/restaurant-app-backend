using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantObservationProcessor(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelObservationJobs jobs, ISandboxOrders orders, ITenantObservationClient tenant, TimeProvider clock) : ITenantObservationProcessor
{
    public async Task<bool> Process(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused) return false;
        var store = options.Store;
        var job = await jobs.Claim(webhook.Value.ClientId, store.StoreId, store.TenantId, cancellationToken);
        if (job is null) return false;
        try
        {
            var body = await orders.Read(job.ExternalOrderId.ToString("D"), cancellationToken);
            var state = ProviderJson.Text(body, "current_state");
            if (ProviderJson.Text(body, "id") != job.ExternalOrderId.ToString("D")
                || !body.TryGetProperty("store", out var providerStore) || ProviderJson.Text(providerStore, "id") != store.StoreId.ToString("D")
                || state is not ("CREATED" or "ACCEPTED" or "CANCELED" or "DENIED" or "FINISHED"))
                throw new ChannelConsoleException(502, "Canonical marketplace observation was not confirmed.");
            var observation = new TenantOrderObservation("uber-eats", store.StoreId.ToString("D"), job.ExternalOrderId.ToString("D"),
                state, ProviderJson.Hash(body), clock.GetUtcNow());
            var terminal = await tenant.Observe(store, job.TenantOrderId, observation, cancellationToken);
            await jobs.Finish(job, state, observation.CanonicalHash, observation.ObservedAt, terminal,
                clock.GetUtcNow().AddSeconds(options.RetrySeconds), cancellationToken);
        }
        catch (ChannelConsoleException) { await Retry(job, cancellationToken); }
        catch (HttpRequestException) { await Retry(job, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { await Retry(job, cancellationToken); }
        return true;
    }

    private Task<bool> Retry(ChannelObservationJob job, CancellationToken cancellationToken)
        => jobs.Finish(job, null, null, null, false, clock.GetUtcNow().AddSeconds(settings.Value.RetrySeconds), cancellationToken);
}
