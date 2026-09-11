using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>One order or additive print-job outcome reported by a printer-app. The device id is
/// taken from the <c>X-Device-Id</c> header, never the body, so it can't be spoofed per-item.
/// <para>JobId, Revision and JobType are nullable for wire compatibility with existing clients.
/// When omitted, the legacy natural key is (OrderId, Target). New update jobs must send all three.
/// </para></summary>
public record PrintAckDto(
    Guid OrderId,
    DevicePrintTarget Target,
    DevicePrintStatus Status,
    DateTime ReceivedAt,
    DateTime? PrintedAt,
    string? FailureReason,
    int Copies,
    Guid? JobId = null,
    int? Revision = null,
    DevicePrintJobType? JobType = null
);
