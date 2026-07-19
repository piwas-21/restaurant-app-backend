using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>One diagnostic event reported by a printer-app. <see cref="ClientEventId"/> is the
/// device-generated idempotency key that makes at-least-once outbox delivery safe.</summary>
public record DeviceEventDto(
    string ClientEventId,
    DateTime OccurredAt,
    DeviceEventLevel Level,
    string? Code,
    string Message,
    string? Context
);
