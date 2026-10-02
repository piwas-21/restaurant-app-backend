using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelManagementOperations(ITenantChannelSummaryService summary,
    ITenantChannelCatalogueService catalogue, ITenantChannelAvailabilityService availability,
    ITenantChannelExceptionService exceptions, ITenantChannelConnectionService connection)
    : ITenantChannelManagementOperations
{
    public Task<JsonElement> Summary(CancellationToken cancellationToken) => summary.Summary(cancellationToken);
    public Task<JsonElement> Catalogue(CancellationToken cancellationToken) => catalogue.Catalogue(cancellationToken);
    public Task<JsonElement> SaveDraft(TenantManagementDraftRequest request, Guid actorId, CancellationToken cancellationToken)
        => catalogue.SaveDraft(request, actorId, cancellationToken);
    public Task<JsonElement> Preview(TenantManagementPreviewRequest request, Guid actorId, CancellationToken cancellationToken)
        => catalogue.Preview(request, actorId, cancellationToken);
    public Task<JsonElement> Publish(TenantManagementPublishRequest request, Guid actorId, CancellationToken cancellationToken)
        => catalogue.Publish(request, actorId, cancellationToken);
    public Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken)
        => catalogue.Publication(publicationId, cancellationToken);
    public Task<JsonElement> Availability(CancellationToken cancellationToken) => availability.Availability(cancellationToken);
    public Task<JsonElement> Pause(TenantManagementPauseRequest request, Guid actorId, CancellationToken cancellationToken)
        => availability.Pause(request, actorId, cancellationToken);
    public Task<JsonElement> Resume(Guid actorId, CancellationToken cancellationToken) => availability.Resume(actorId, cancellationToken);
    public Task<JsonElement> Exceptions(string cursor, CancellationToken cancellationToken) => exceptions.Exceptions(cursor, cancellationToken);
    public Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken) => exceptions.Exception(exceptionId, cancellationToken);
    public Task<JsonElement> Reconcile(Guid exceptionId, Guid actorId, CancellationToken cancellationToken)
        => exceptions.Reconcile(exceptionId, actorId, cancellationToken);
    public Task<JsonElement> Disconnect(Guid storeId, Guid actorId, CancellationToken cancellationToken)
        => connection.Disconnect(storeId, actorId, cancellationToken);
}
