using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>Admin read of one device diagnostic event. <see cref="ReceivedAt"/> is when the backend
/// ingested it; <see cref="OccurredAt"/> is the device clock.</summary>
public record DeviceEventLogDto(
    Guid Id,
    string ClientEventId,
    DateTime OccurredAt,
    DeviceEventLevel Level,
    string? Code,
    string Message,
    string? Context,
    DateTime ReceivedAt
);
