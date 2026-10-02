using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelOrderProcessingPolicy(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ICatalogueMappingResolver mappings, IChannelManagementConnectionState connectionState,
    ITenantCataloguePublication catalogue) : ITenantChannelOrderProcessingPolicy
{
    public async Task<CreatedOrderRecoveryContext?> ResolveRecovery(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused || !options.RecoverCreatedOrders) return null;
        if (await IsDisconnected(options.Store, cancellationToken)) return null;
        var store = await mappings.Active(cancellationToken);
        if (webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != store.StoreId)
            throw new ChannelConsoleException(409, "Recovery requires the approved sandbox store binding.");
        return new(store, webhook.Value.ClientId, options.RecoveryListLimit, options.EnrollmentStartedAt);
    }

    public async Task<TenantOrderImportContext?> ResolveImport(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused) return null;
        if (await IsDisconnected(options.Store, cancellationToken)) return null;
        if (options.UseTenantCatalogue) await catalogue.RequireActive(cancellationToken);
        var store = await mappings.Active(cancellationToken);
        return new(store, webhook.Value.ClientId, options.EnrollmentStartedAt, options.PayloadRetentionDays, options.RetrySeconds);
    }

    public Task<TenantStoreBinding> HistoricalStore(TenantOrderImportContext context, string catalogueRevision,
        CancellationToken cancellationToken)
        => ResolveHistoricalStore(context, catalogueRevision, cancellationToken);

    private async Task<TenantStoreBinding> ResolveHistoricalStore(TenantOrderImportContext context, string catalogueRevision,
        CancellationToken cancellationToken)
    {
        var store = await mappings.ForRevision(catalogueRevision, cancellationToken);
        if (store.StoreId != context.Store.StoreId || store.TenantId != context.Store.TenantId
            || store.CatalogueRevision != catalogueRevision)
            throw new ChannelConsoleException(409, "Historical marketplace mapping belongs to a different store or tenant binding.");
        return store;
    }

    private async Task<bool> IsDisconnected(TenantStoreBinding configured, CancellationToken cancellationToken)
    {
        var binding = new AvailabilityBinding(webhook.Value.ClientId, configured.StoreId, configured.TenantId, configured.CatalogueRevision);
        return (await connectionState.Read(binding, cancellationToken)).IsDisconnected;
    }
}
