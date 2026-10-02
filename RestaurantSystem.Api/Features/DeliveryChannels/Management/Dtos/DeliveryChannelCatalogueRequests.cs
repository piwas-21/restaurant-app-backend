namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelCatalogueDraftRequest(
    string? ExpectedDraftRevision,
    IReadOnlyList<DeliveryChannelDraftMappingDto> Items);

public sealed record DeliveryChannelDraftMappingDto(
    string ProviderItemId,
    Guid ProductId,
    Guid? VariationId);

public sealed record DeliveryChannelCatalogueDraftDto(
    string DraftRevision,
    string MappingRevision,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DeliveryChannelMappingRowDto> Items);

public sealed record DeliveryChannelPreviewRequest(string DraftRevision);

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
    string CurrentServiceHoursStatus);

public sealed record DeliveryChannelPublishRequest(string DraftRevision, string PublicationRevision);
