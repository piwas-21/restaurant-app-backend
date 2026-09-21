using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordHeartbeatCommand;

/// <summary>
/// Upserts the calling printer-app installation's fleet status. <see cref="DeviceId"/> comes from
/// the <c>X-Device-Id</c> header (the controller injects it); the body carries the rest. Carries
/// only non-secret config — never the printer-feed API key.
/// </summary>
public record RecordHeartbeatCommand(
    // Populated from the X-Device-Id header by the controller, never the body — ignore for JSON
    // binding + OpenAPI so it can't be set (or spoofed) via the request payload.
    [property: JsonIgnore] string DeviceId,
    string? Label,
    string? TenantSlug,
    string? Platform,
    string? AppVersion,
    bool? FeedRunning,
    DateTime? LastSuccessfulPollAt,
    string? ApiBaseUrl,
    string? KitchenPrinter,
    string? CashierPrinter,
    List<PrinterTargetCapabilityDto>? TargetCapabilities = null,
    DeviceKitchenRoutingMode? KitchenRoutingMode = null
) : ICommand<ApiResponse<bool>>;

public class RecordHeartbeatCommandHandler
    : ICommandHandler<RecordHeartbeatCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RecordHeartbeatCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<bool>> Handle(
        RecordHeartbeatCommand command, CancellationToken cancellationToken)
    {
        var auditId = _currentUserService.GetAuditIdentifier();
        var device = await _context.PrinterDevices
            .FirstOrDefaultAsync(d => d.DeviceId == command.DeviceId, cancellationToken);

        if (device is null)
        {
            device = new PrinterDevice
            {
                DeviceId = command.DeviceId,
                CreatedBy = auditId,
            };
            _context.PrinterDevices.Add(device);
        }

        device.Label = command.Label;
        device.TenantSlug = command.TenantSlug;
        device.Platform = command.Platform;
        device.AppVersion = command.AppVersion;
        device.FeedRunning = command.FeedRunning ?? false;
        // The client reports the last completed poll, but the server owns the upper bound. A clock
        // set into the future must never make a device look fresh indefinitely.
        var now = DateTime.UtcNow;
        device.LastSuccessfulPollAt = BoundPollTimestamp(command.LastSuccessfulPollAt, now);
        device.ApiBaseUrl = command.ApiBaseUrl;
        device.KitchenPrinter = command.KitchenPrinter;
        device.CashierPrinter = command.CashierPrinter;
        if (command.KitchenRoutingMode.HasValue)
        {
            device.KitchenRoutingMode = command.KitchenRoutingMode.Value;
        }
        device.LastHeartbeatAt = now;

        if (command.TargetCapabilities is not null)
        {
            await UpsertCapabilitiesAsync(device.DeviceId, command.TargetCapabilities, auditId,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessWithData(true, "Heartbeat recorded.");
    }

    private static DateTime? BoundPollTimestamp(DateTime? value, DateTime serverNow)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var candidate = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        return candidate > serverNow ? serverNow : candidate;
    }

    private async Task UpsertCapabilitiesAsync(
        string deviceId, IReadOnlyCollection<PrinterTargetCapabilityDto> reports,
        string auditId, CancellationToken cancellationToken)
    {
        var normalizedReports = reports
            .GroupBy(report => report.Target)
            .Select(group => group.Last())
            .ToList();
        var reportedAt = DateTime.UtcNow;
        if (normalizedReports.Count == 0)
        {
            // An omitted list means an old client and deliberately preserves the last report. An
            // explicit empty list comes from a capability-aware client with no usable targets;
            // mark its previous rows unsupported immediately so a just-disabled printer cannot
            // remain ready during the heartbeat freshness window.
            var priorReports = await _context.PrinterDeviceTargetCapabilities
                .Where(capability => capability.DeviceId == deviceId)
                .ToListAsync(cancellationToken);
            foreach (var capability in priorReports)
            {
                capability.IsSupported = false;
                capability.IsConfigured = false;
                capability.AutoPrintEnabled = false;
                capability.ReportedAt = reportedAt;
                capability.UpdatedAt = reportedAt;
                capability.UpdatedBy = auditId;
            }

            return;
        }

        var existing = await _context.PrinterDeviceTargetCapabilities
            .Where(capability => capability.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        foreach (var report in normalizedReports)
        {
            var capability = existing.SingleOrDefault(item => item.Target == report.Target);
            if (capability is null)
            {
                capability = new PrinterDeviceTargetCapability
                {
                    DeviceId = deviceId,
                    Target = report.Target,
                    CreatedBy = auditId
                };
                _context.PrinterDeviceTargetCapabilities.Add(capability);
                existing.Add(capability);
            }

            capability.IsSupported = report.IsSupported;
            capability.IsConfigured = report.IsConfigured;
            capability.AutoPrintEnabled = report.AutoPrintEnabled;
            capability.PrinterName = report.PrinterName;
            capability.ReportedAt = reportedAt;
            capability.UpdatedAt = reportedAt;
            capability.UpdatedBy = auditId;
        }

        // A capability-aware heartbeat is a complete snapshot, not a patch. If a printer target
        // disappears from the list (for example the front station was unplugged), leaving the old
        // row ready would keep routing new orders to a destination the device can no longer print.
        var reportedTargets = normalizedReports.Select(report => report.Target).ToHashSet();
        foreach (var capability in existing.Where(item => !reportedTargets.Contains(item.Target)))
        {
            capability.IsSupported = false;
            capability.IsConfigured = false;
            capability.AutoPrintEnabled = false;
            capability.ReportedAt = reportedAt;
            capability.UpdatedAt = reportedAt;
            capability.UpdatedBy = auditId;
        }
    }
}
