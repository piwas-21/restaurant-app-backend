namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelCatalogueDraftDto(
    string DraftRevision,
    string MappingRevision,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DeliveryChannelMappingRowDto> Items)
{
    public string SelectionMode { get; init; } = "fixedItemsV1";
    public string SourceRevision { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<Guid> SelectedCategoryIds { get; init; } = [];
    public IReadOnlyList<DeliveryChannelCategoryOverrideDto> ItemOverrides { get; init; } = [];
    public IReadOnlyList<DeliveryChannelCategoryDto> Categories { get; init; } = [];
    public IReadOnlyList<DeliveryChannelCategoryItemDto> SelectedItems { get; init; } = [];
}

public sealed record DeliveryChannelPreviewDto(
    string DraftRevision,
    string MappingRevision,
    string SourceRevision,
    string PublicationRevision,
    string Currency,
    bool CanPublish,
    IReadOnlyList<DeliveryChannelMappingRowDto> Items,
    IReadOnlyList<DeliveryChannelServiceHoursDayDto> ServiceAvailability,
    bool ServiceHoursEditable,
    IReadOnlyList<string> BlockingCodes,
    IReadOnlyList<string> WarningCodes,
    string ServiceHoursStatus,
    IReadOnlyList<DeliveryChannelServiceHoursDayDto> CurrentServiceAvailability,
    string CurrentServiceHoursStatus)
{
    public string SelectionMode { get; init; } = "fixedItemsV1";
    public IReadOnlyList<DeliveryChannelCategoryItemDto> SelectedItems { get; init; } = [];
    public DeliveryChannelTaxProfileDto? TaxProfile { get; init; }
    public string TaxProfileRevision { get; init; } = string.Empty;
}
