using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantImportProcessor(IChannelImportJobs jobs, ISandboxOrders orders, IUberOrderNormalizer normalizer,
    ISandboxCrypto crypto, ITenantOrderClient tenant, TimeProvider clock, ITenantChannelOrderProcessingPolicy policy) : ITenantImportProcessor
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<bool> Process(CancellationToken cancellationToken)
    {
        // Retention continues during pause/disable and application rollback with this gateway version.
        await jobs.ExpirePayloads(cancellationToken);
        var context = await policy.ResolveImport(cancellationToken);
        if (context is null) return false;
        await jobs.Discover(context.ClientId, context.Store.StoreId, context.Store.TenantId, context.Store.CatalogueRevision,
            context.EnrollmentStartedAt, cancellationToken);
        var job = await jobs.Claim(context.ClientId, context.Store.StoreId, context.Store.TenantId, cancellationToken);
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
                var historical = await policy.HistoricalStore(context, job.CatalogueRevision, cancellationToken);
                request = normalizer.Normalize(await orders.Read(job.OrderId.ToString("D"), cancellationToken), job.OrderId, historical);
                var json = JsonSerializer.Serialize(request, Wire);
                if (!await jobs.Prepare(job, crypto.Protect(json, Purpose(job)), crypto.Hash(json),
                    now.AddDays(context.PayloadRetentionDays), cancellationToken)) return true;
            }
            var imported = await tenant.Import(context.Store, request, cancellationToken);
            await jobs.Imported(job, imported.OrderId, cancellationToken);
        }
        catch (ChannelConsoleException exception)
        {
            var terminal = IsTerminalContractRejection(exception);
            await jobs.Defer(job, terminal ? "ContractRejected" : "DeliveryUncertain", now.AddSeconds(context.RetrySeconds), terminal, cancellationToken);
        }
        return true;
    }

    private static bool IsTerminalContractRejection(ChannelConsoleException exception)
        => exception.Status is 400 or 403 or 404 or 409 or 413 or 422;

    private static string Purpose(ChannelImportJob job)
        => $"tenant-import:{job.ClientId}:{job.StoreId:D}:{job.OrderId:D}:{job.TenantId}:{job.CatalogueRevision}";
}
