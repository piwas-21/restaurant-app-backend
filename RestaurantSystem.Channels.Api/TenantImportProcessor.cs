using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantImportProcessor(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelImportJobs jobs, ISandboxOrders orders, IUberOrderNormalizer normalizer, ISandboxCrypto crypto,
    ITenantOrderClient tenant, TimeProvider clock, ITenantCataloguePublication? catalogue = null) : ITenantImportProcessor
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<bool> Process(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        // Retention continues during pause/disable and application rollback with this gateway version.
        await jobs.ExpirePayloads(cancellationToken);
        if (!options.Enabled || options.Paused) return false;
        await RequirePublication(options, cancellationToken);
        var store = options.Store;
        await jobs.Discover(webhook.Value.ClientId, store.StoreId, store.TenantId, store.CatalogueRevision,
            options.EnrollmentStartedAt, cancellationToken);
        var job = await jobs.Claim(webhook.Value.ClientId, store.StoreId, store.TenantId, cancellationToken);
        if (job is null) return false;
        var now = clock.GetUtcNow();
        try
        {
            TenantOrderRequest request;
            if (job.EncryptedRequest is not null)
            {
                if (job.PayloadExpiresAt <= now) throw UberOrderValue.Unsupported();
                request = JsonSerializer.Deserialize<TenantOrderRequest>(crypto.Unprotect(job.EncryptedRequest, Purpose(job)), Wire)
                    ?? throw UberOrderValue.Unsupported();
            }
            else
            {
                if (job.CatalogueRevision != store.CatalogueRevision) throw UberOrderValue.Unsupported();
                request = normalizer.Normalize(await orders.Read(job.OrderId.ToString("D"), cancellationToken), job.OrderId, store);
                var json = JsonSerializer.Serialize(request, Wire);
                if (!await jobs.Prepare(job, crypto.Protect(json, Purpose(job)), crypto.Hash(json),
                    now.AddDays(options.PayloadRetentionDays), cancellationToken)) return true;
            }
            var imported = await tenant.Import(store, request, cancellationToken);
            await jobs.Imported(job, imported.OrderId, cancellationToken);
        }
        catch (ChannelConsoleException exception)
        {
            var terminal = exception.Status is 400 or 403 or 404 or 409 or 413 or 422;
            await jobs.Defer(job, terminal ? "ContractRejected" : "DeliveryUncertain", now.AddSeconds(options.RetrySeconds), terminal, cancellationToken);
        }
        return true;
    }

    private async Task RequirePublication(TenantBridgeSettings options, CancellationToken cancellationToken)
    {
        if (options.UseTenantCatalogue)
        {
            if (catalogue is null) throw new ChannelConsoleException(409, "Tenant catalogue publication is unavailable.");
            await catalogue.RequireActive(cancellationToken);
        }
    }

    private static string Purpose(ChannelImportJob job)
        => $"tenant-import:{job.ClientId}:{job.StoreId:D}:{job.OrderId:D}:{job.TenantId}:{job.CatalogueRevision}";
}
