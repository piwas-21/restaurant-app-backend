using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelCatalogueCategoryDto(Guid CategoryId, string Name, int DisplayOrder,
    int TotalItemCount, int SupportedItemCount, int UnsupportedItemCount, bool Active);

public sealed record ChannelCatalogueSelectionCategoryDto(Guid CategoryId, string Name, int DisplayOrder,
    int TotalItemCount, int SupportedItemCount, int UnsupportedItemCount, int SelectedItemCount,
    int SelectedUnsupportedItemCount, bool Active);

public sealed record ChannelCatalogueCategoriesSnapshot(string Provider, string StoreId, string Currency,
    bool IsSandbox, string Language, string Revision, IReadOnlyList<ChannelCatalogueCategoryDto> Categories)
{
    public string CategoryBasis { get; init; } = "primaryCategory";
    public int MaximumCategoryCount { get; init; } = ExternalOrderLimits.MaximumCatalogueCategories;
    public int MaximumItemOverrideCount { get; init; } = ExternalOrderLimits.MaximumCatalogueItemOverrides;
    public bool SourceChanged { get; init; }
    public IReadOnlyList<Guid> RemovedCategoryIds { get; init; } = [];
    public IReadOnlyList<ChannelCatalogueRemovedItemReferenceDto> RemovedItems { get; init; } = [];
    public IReadOnlyList<ChannelCatalogueRemovedItemOverrideDto> RemovedItemOverrides { get; init; } = [];
    public IReadOnlyList<ChannelCatalogueItemStatusDto> ItemStatuses { get; init; } = [];
}

public sealed record ChannelCatalogueItemStatusDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, bool Supported);

public sealed record ChannelCatalogueRemovedItemReferenceDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, string Reason);

public sealed record ChannelCatalogueRemovedItemOverrideDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelCatalogueCategoryReferencesRequest(
    [property: JsonRequired] string ExpectedSourceRevision,
    [property: JsonRequired] IReadOnlyList<Guid> CategoryIds,
    [property: JsonRequired] IReadOnlyList<ChannelCatalogueItemReferenceRequest> ItemReferences,
    [property: JsonRequired] IReadOnlyList<ChannelCatalogueItemOverrideRequest> ItemOverrides);

public sealed record ChannelCatalogueItemReferenceRequest([property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId, [property: JsonRequired] Guid? CategoryId);
