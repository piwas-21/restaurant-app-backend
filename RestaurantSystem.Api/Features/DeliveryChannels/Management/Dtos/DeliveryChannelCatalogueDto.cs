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
    string CurrentServiceHoursStatus);

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
