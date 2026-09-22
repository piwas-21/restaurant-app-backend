using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>Printer-app's non-secret readiness report for one logical target.</summary>
public sealed record PrinterTargetCapabilityDto(
    DevicePrintTarget Target,
    bool IsSupported,
    bool IsConfigured,
    bool AutoPrintEnabled,
    string? PrinterName);
