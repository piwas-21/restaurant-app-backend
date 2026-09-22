using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Authoritative lifecycle of one server-owned printer destination.</summary>
public sealed record OrderRoutingStateDto(
    Guid Id,
    Guid JobId,
    int Revision,
    DevicePrintTarget Target,
    DevicePrintStatus Status,
    string? DeviceId,
    string? FailureReason,
    DateTime? LastAcknowledgedAt,
    int Version,
    bool IsRequired);
