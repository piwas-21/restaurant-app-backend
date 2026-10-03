namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelCatalogueDto(
    Guid StoreId,
    string Currency,
    string MappingRevision,
    string DraftRevision,
    string? SourceRevision,
    bool CanPublish,
    IReadOnlyList<DeliveryChannelMappingRowDto> Items,
    IReadOnlyList<DeliveryChannelServiceHoursDayDto> ServiceAvailability,
    bool ServiceHoursEditable,
    IReadOnlyList<string> BlockingCodes,
    IReadOnlyList<string> WarningCodes,
    DeliveryChannelPublicationSummaryDto? LatestPublication,
    string ServiceHoursStatus,
    IReadOnlyList<DeliveryChannelServiceHoursDayDto> CurrentServiceAvailability,
    string CurrentServiceHoursStatus)
{
    public string SelectionMode { get; init; } = "fixedItemsV1";
    public IReadOnlyList<DeliveryChannelCategoryDto> Categories { get; init; } = [];
    public IReadOnlyList<DeliveryChannelCategoryItemDto> SelectedItems { get; init; } = [];
    public bool SourceChanged { get; init; }
    public string? DraftSourceRevision { get; init; }
    public DeliveryChannelTaxProfileDto? TaxProfile { get; init; }
    public string TaxProfileRevision { get; init; } = string.Empty;
}

public sealed record DeliveryChannelTaxProfileDto(string Source, string ProfileRevision,
    decimal VatRatePercentage, bool MerchantVerificationRequired);

public sealed record DeliveryChannelMappingRowDto(
    string ProviderItemId,
    string ProviderItemName,
    Guid? ProductId,
    Guid? VariationId,
    string? ProductName,
    string? VariationName,
    int? TenantPriceMinor,
    int? ProviderPriceMinor,
    string Currency,
    bool Available,
    string MappingStatus,
    string? BlockReason,
    string ProviderPriceStatus = "unknown");

public sealed record DeliveryChannelServiceHoursDayDto(
    string DayOfWeek,
    IReadOnlyList<DeliveryChannelTimePeriodDto> TimePeriods);

public sealed record DeliveryChannelTimePeriodDto(string StartTime, string EndTime);
