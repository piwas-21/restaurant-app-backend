namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelExceptionInboxDto(
    IReadOnlyList<DeliveryChannelExceptionDto> Items,
    string? NextCursor,
    DateTimeOffset CheckedAt);

public sealed record DeliveryChannelExceptionDto(
    Guid Id,
    string Kind,
    string Severity,
    string Status,
    string Code,
    string Title,
    string? Detail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ProviderOrderId,
    Guid? LocalOrderId,
    bool CanReconcile,
    bool AutomaticRetryBlocked);

public sealed record DeliveryChannelReconcileResultDto(
    Guid OperationId,
    string Status,
    string? Code,
    DateTimeOffset? ObservedAt,
    bool ProviderRequestSent);

public sealed record DeliveryChannelDisconnectResultDto(
    string Status,
    bool ProviderManagerRelinquished,
    bool LocalBridgePaused,
    string? ResultCode,
    DateTimeOffset CompletedAt);

public sealed record DeliveryChannelDisconnectRequest(Guid StoreId);
