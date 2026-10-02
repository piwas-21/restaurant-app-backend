namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelAvailabilityDto(
    bool Enabled,
    bool Paused,
    DateTimeOffset? PausedUntil,
    DateTimeOffset CheckedAt,
    string? StoreStatus,
    IReadOnlyList<DeliveryChannelItemAvailabilityDto> Items);

public sealed record DeliveryChannelItemAvailabilityDto(
    string ProviderItemId,
    Guid ProductId,
    Guid? VariationId,
    bool DesiredAvailable,
    bool? ConfirmedAvailable,
    string State,
    DateTimeOffset? VerifiedAt,
    bool IsStale,
    string? ReasonCode);

public sealed record DeliveryChannelPauseRequest(int? DurationMinutes);

public sealed record DeliveryChannelAvailabilityActionDto(
    string State,
    DateTimeOffset? EffectiveUntil,
    bool ProviderConfirmed,
    string? ResultCode);
