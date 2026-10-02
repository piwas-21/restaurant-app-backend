using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public interface ITenantChannelExceptionData
{
    Task<CataloguePublication?> LatestPublication(CancellationToken cancellationToken);
    Task<CataloguePublication?> FindPublication(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelAvailabilityState>> AvailabilityStates(CancellationToken cancellationToken);
    Task<TenantStoreBinding> ActiveStore(CancellationToken cancellationToken);
    Task<JsonElement> Imports(string cursor, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelManagementAuditRecord>> Audit(CancellationToken cancellationToken);
    Task<TenantOAuthFlow?> OAuthFlow(Guid id, CancellationToken cancellationToken);
    Task<ChannelManagementConnectionState> Connection(CancellationToken cancellationToken);
}

public sealed class TenantChannelExceptionData(TenantManagementContext context,
    ITenantCatalogueManagementState catalogue, ITenantChannelAvailabilityState availability,
    IChannelImportView imports, IChannelManagementAudit audit, ITenantOAuthFlows flows,
    IChannelManagementConnectionState connection) : ITenantChannelExceptionData
{
    public Task<CataloguePublication?> LatestPublication(CancellationToken cancellationToken)
        => catalogue.Latest(context.Binding(), cancellationToken);
    public Task<CataloguePublication?> FindPublication(Guid id, CancellationToken cancellationToken)
        => catalogue.Find(context.Binding(), id, cancellationToken);
    public async Task<IReadOnlyList<ChannelAvailabilityState>> AvailabilityStates(CancellationToken cancellationToken)
    {
        var store = await availability.Active(cancellationToken);
        return await availability.Read(context.Binding(store), cancellationToken);
    }
    public Task<TenantStoreBinding> ActiveStore(CancellationToken cancellationToken) => availability.Active(cancellationToken);
    public Task<JsonElement> Imports(string cursor, CancellationToken cancellationToken) => imports.Read(cursor, cancellationToken);
    public Task<IReadOnlyList<ChannelManagementAuditRecord>> Audit(CancellationToken cancellationToken)
        => audit.Read(context.Binding(), null, 50, cancellationToken);
    public Task<TenantOAuthFlow?> OAuthFlow(Guid id, CancellationToken cancellationToken)
        => flows.Read(context.Binding(), id, cancellationToken);
    public Task<ChannelManagementConnectionState> Connection(CancellationToken cancellationToken)
        => connection.Read(context.Binding(), cancellationToken);
}
