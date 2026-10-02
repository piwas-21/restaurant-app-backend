using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record TenantManagementDraftRequest(string? ExpectedDraftRevision,
    IReadOnlyList<TenantManagementMapping> Items);
public sealed record TenantManagementMapping(string ProviderItemId, Guid ProductId, Guid? VariationId);
public sealed record TenantManagementPreviewRequest(string DraftRevision);
public sealed record TenantManagementPublishRequest(string DraftRevision, string PublicationRevision);
public sealed record TenantManagementPauseRequest(int? DurationMinutes);
public sealed record TenantManagementDisconnectRequest(Guid StoreId);

public interface ITenantChannelManagementOperations
{
    Task<JsonElement> Summary(CancellationToken cancellationToken);
    Task<JsonElement> Catalogue(CancellationToken cancellationToken);
    Task<JsonElement> SaveDraft(TenantManagementDraftRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Preview(TenantManagementPreviewRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Publish(TenantManagementPublishRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken);
    Task<JsonElement> Availability(CancellationToken cancellationToken);
    Task<JsonElement> Pause(TenantManagementPauseRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Resume(Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Exceptions(string cursor, CancellationToken cancellationToken);
    Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken);
    Task<JsonElement> Reconcile(Guid exceptionId, Guid actorId, CancellationToken cancellationToken);
    Task<JsonElement> Disconnect(Guid storeId, Guid actorId, CancellationToken cancellationToken);
}
