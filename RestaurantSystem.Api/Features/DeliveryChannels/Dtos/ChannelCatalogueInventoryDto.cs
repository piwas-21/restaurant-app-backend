using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelCatalogueInventoryItemDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid? CategoryId, string? CategoryName, int? CategoryDisplayOrder, int ItemDisplayOrder,
    string Name, string Description, string? VariationName, int? PriceMinor, bool Available, bool Supported, string BlockReason,
    string SourceFingerprint);

public sealed record ChannelCatalogueInventorySnapshot(string Provider, string StoreId, string Currency,
    bool IsSandbox, string Language, string Revision, IReadOnlyList<ChannelCatalogueCategoryDto> Categories,
    IReadOnlyList<ChannelCatalogueInventoryItemDto> Items);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelCatalogueItemOverrideRequest([property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId,
    [property: JsonRequired] Guid? CategoryId, [property: JsonRequired] bool Selected);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelCatalogueSelectionSnapshotRequest(
    [property: JsonRequired] string ExpectedSourceRevision,
    [property: JsonRequired] IReadOnlyList<Guid> CategoryIds,
    [property: JsonRequired] IReadOnlyList<ChannelCatalogueItemOverrideRequest> ItemOverrides);

public sealed record ChannelCatalogueSelectionSnapshot(string Provider, string StoreId, string Currency,
    bool IsSandbox, string Language, string Revision, IReadOnlyList<ChannelCatalogueSelectionCategoryDto> Categories,
    IReadOnlyList<Guid> SelectedCategoryIds, IReadOnlyList<ChannelCatalogueItemOverrideSnapshotDto> ItemOverrides,
    IReadOnlyList<ChannelCatalogueInventoryItemDto> Items);

public sealed record ChannelCatalogueItemOverrideSnapshotDto(Guid ProductId, Guid? VariationId, Guid? CategoryId,
    bool Selected, string SelectionKey, string SourceFingerprint, bool Supported);
