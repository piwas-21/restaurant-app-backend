using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ITenantChannelSummaryService
{
    Task<JsonElement> Summary(CancellationToken cancellationToken);
}

public interface ITenantChannelCatalogueService
{
    Task<JsonElement> Catalogue(CancellationToken cancellationToken);
    Task<JsonElement> SaveDraft(TenantManagementDraftRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Preview(TenantManagementPreviewRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Publish(TenantManagementPublishRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken);
}

public interface ITenantChannelAvailabilityService
{
    Task<JsonElement> Availability(CancellationToken cancellationToken);
    Task<JsonElement> Pause(TenantManagementPauseRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Resume(Guid actorId, CancellationToken cancellationToken);
}

public interface ITenantChannelExceptionService
{
    Task<JsonElement> Exceptions(string cursor, CancellationToken cancellationToken);
    Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken);
    Task<JsonElement> Reconcile(Guid exceptionId, Guid actorId, CancellationToken cancellationToken);
}

public interface ITenantChannelConnectionService
{
    Task<JsonElement> Disconnect(Guid storeId, Guid actorId, CancellationToken cancellationToken);
}
