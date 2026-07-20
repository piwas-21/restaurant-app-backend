using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>One order/target print outcome reported by a printer-app. The device id is taken from
/// the <c>X-Device-Id</c> header, never the body, so it can't be spoofed per-item.</summary>
public record PrintAckDto(
    Guid OrderId,
    DevicePrintTarget Target,
    DevicePrintStatus Status,
    DateTime ReceivedAt,
    DateTime? PrintedAt,
    string? FailureReason,
    int Copies
);
