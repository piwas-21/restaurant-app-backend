using System.Text.Json.Serialization;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record TenantManagementDraftRequest(string? ExpectedDraftRevision,
    IReadOnlyList<TenantManagementMapping> Items)
{
    public string ExpectedSourceRevision { get; init; } = string.Empty;
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];
    public IReadOnlyList<TenantManagementItemOverride> ItemOverrides { get; init; } = [];
}
public sealed record TenantManagementMapping(string ProviderItemId, Guid ProductId, Guid? VariationId);
public sealed record TenantManagementItemOverride(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId,
    [property: JsonRequired] Guid? CategoryId,
    [property: JsonRequired] bool Selected);
public sealed record TenantManagementPreviewRequest(string DraftRevision);
public sealed record TenantManagementPublishRequest(string DraftRevision, string PublicationRevision)
{
    public bool ConfirmedTaxProfile { get; init; }
    public string TaxProfileRevision { get; init; } = string.Empty;
}
public sealed record TenantManagementPauseRequest(int? DurationMinutes);
public sealed record TenantManagementDisconnectRequest([property: JsonRequired] Guid StoreId);

public interface ITenantChannelManagementOperations
{
    Task<JsonElement> Summary(CancellationToken cancellationToken);
    Task<JsonElement> Catalogue(CancellationToken cancellationToken);
    Task<JsonElement> CatalogueCategories(CancellationToken cancellationToken);
    Task<JsonElement> CheckCategorySelection(TenantManagementCategoryChangesRequest request, CancellationToken cancellationToken);
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
