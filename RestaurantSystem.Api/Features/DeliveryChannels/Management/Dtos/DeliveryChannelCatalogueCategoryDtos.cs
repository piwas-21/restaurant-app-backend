using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelCategoriesDto(string SelectionMode, int MaximumSelectedItemCount,
    string SourceRevision, string Language, IReadOnlyList<DeliveryChannelCategoryDto> Categories,
    bool SourceChanged, DeliveryChannelCategoryDraftDto? Draft)
{
    public string CategoryBasis { get; init; } = "primaryCategory";
    public int MaximumCategoryCount { get; init; } = ExternalOrderLimits.MaximumCatalogueCategories;
    public int MaximumItemOverrideCount { get; init; } = ExternalOrderLimits.MaximumCatalogueItemOverrides;
    public string? DraftRevision { get; init; }
    public IReadOnlyList<Guid> RemovedCategoryIds { get; init; } = [];
    public IReadOnlyList<DeliveryChannelRemovedItemOverrideDto> RemovedItems { get; init; } = [];
    public IReadOnlyList<DeliveryChannelRemovedItemOverrideDto> RemovedItemOverrides { get; init; } = [];
    public IReadOnlyList<DeliveryChannelCategoryItemStatusDto> ItemStatuses { get; init; } = [];
}

public sealed record DeliveryChannelRemovedItemOverrideDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, string Reason);

public sealed record DeliveryChannelCategoryChangesRequest(
    [property: JsonRequired] string ExpectedSourceRevision,
    [property: JsonRequired] IReadOnlyList<Guid> CategoryIds,
    [property: JsonRequired] IReadOnlyList<DeliveryChannelCategoryItemReferenceDto> ItemReferences,
    [property: JsonRequired] IReadOnlyList<DeliveryChannelItemOverrideDto> ItemOverrides);

public sealed record DeliveryChannelCategoryItemReferenceDto([property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId, [property: JsonRequired] Guid? CategoryId);

public sealed record DeliveryChannelCategoryChangesDto(string SourceRevision, string Language, bool SourceChanged,
    IReadOnlyList<DeliveryChannelCategoryDto> Categories, IReadOnlyList<Guid> RemovedCategoryIds,
    IReadOnlyList<DeliveryChannelRemovedItemOverrideDto> RemovedItems,
    IReadOnlyList<DeliveryChannelRemovedItemOverrideDto> RemovedItemOverrides)
{
    public string CategoryBasis { get; init; } = "primaryCategory";
    public int MaximumCategoryCount { get; init; } = ExternalOrderLimits.MaximumCatalogueCategories;
    public int MaximumItemOverrideCount { get; init; } = ExternalOrderLimits.MaximumCatalogueItemOverrides;
    public IReadOnlyList<DeliveryChannelCategoryItemStatusDto> ItemStatuses { get; init; } = [];
}

public sealed record DeliveryChannelCategoryItemStatusDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, bool Supported);

public sealed record DeliveryChannelCategoryDto(Guid CategoryId, string Name, int DisplayOrder,
    int TotalItemCount, int SupportedItemCount, int UnsupportedItemCount, int SelectedItemCount = 0,
    int SelectedUnsupportedItemCount = 0, string? SelectionState = null, bool Active = true);

public sealed record DeliveryChannelCategoryDraftDto(string DraftRevision, string SourceRevision, string Language,
    IReadOnlyList<Guid> SelectedCategoryIds, IReadOnlyList<DeliveryChannelCategoryOverrideDto> ItemOverrides,
    IReadOnlyList<DeliveryChannelCategoryDto> Categories, IReadOnlyList<DeliveryChannelCategoryItemDto> Items);

public sealed record DeliveryChannelCategoryOverrideDto(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid? CategoryId, bool Selected, string SourceFingerprint = "", bool Supported = false);

public sealed record DeliveryChannelCategoryItemDto(string SelectionKey, string ProviderItemId,
    Guid ProductId, Guid? VariationId, Guid? CategoryId, string? CategoryName, int? CategoryDisplayOrder,
    int ItemDisplayOrder, string Name, string? VariationName, string Description, int? PriceMinor, bool Available,
    bool Supported, string BlockReason, string SourceFingerprint = "");
