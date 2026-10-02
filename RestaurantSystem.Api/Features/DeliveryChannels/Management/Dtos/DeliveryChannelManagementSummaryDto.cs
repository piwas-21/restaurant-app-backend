namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelManagementSummaryDto(
    string Provider,
    bool Enabled,
    bool SandboxOnly,
    string ConnectionStatus,
    string HealthStatus,
    Guid StoreId,
    string Currency,
    bool StoreConfirmed,
    string? StoreDisplayName,
    bool IntegrationEnabled,
    bool IsOrderManager,
    bool PendingMerchantActivation,
    bool RequireManualAcceptance,
    bool Paused,
    DateTimeOffset? CheckedAt,
    string? DegradedReason,
    DeliveryChannelCapabilityDto Capabilities,
    DeliveryChannelPublicationSummaryDto? LatestPublication);

public sealed record DeliveryChannelCapabilityDto(
    bool SupportsSimpleItems,
    bool SupportsVariations,
    bool SupportsModifiers,
    bool SupportsBundles,
    bool SupportsItemAvailability,
    bool SupportsStoreHoursEditing,
    bool SupportsAutomaticAcceptance);

public sealed record DeliveryChannelPublicationSummaryDto(
    Guid Id,
    string MappingRevision,
    string PublicationRevision,
    string State,
    DateTimeOffset? VerifiedAt,
    string? ResultCode);

public sealed record DeliveryChannelPublicationDto(
    Guid Id,
    string MappingRevision,
    string PublicationRevision,
    string SourceRevision,
    string State,
    bool ProviderReadbackVerified,
    string? ProviderMenuHash,
    DateTimeOffset? VerifiedAt,
    string? ResultCode);
