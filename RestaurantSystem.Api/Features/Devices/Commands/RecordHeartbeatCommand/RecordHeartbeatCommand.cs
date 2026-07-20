using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
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
    string? CashierPrinter
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
        var device = await _context.PrinterDevices
            .FirstOrDefaultAsync(d => d.DeviceId == command.DeviceId, cancellationToken);

        if (device is null)
        {
            device = new PrinterDevice
            {
                DeviceId = command.DeviceId,
                CreatedBy = _currentUserService.GetAuditIdentifier(),
            };
            _context.PrinterDevices.Add(device);
        }

        device.Label = command.Label;
        device.TenantSlug = command.TenantSlug;
        device.Platform = command.Platform;
        device.AppVersion = command.AppVersion;
        device.FeedRunning = command.FeedRunning ?? false;
        // Normalise the only client-supplied timestamp to UTC Kind — the column is `timestamptz`
        // and Npgsql rejects a non-UTC Kind. The app reports UTC instants, so relabel (SpecifyKind)
        // rather than convert — matching Groups/UserGroupService's client-DateTime handling.
        device.LastSuccessfulPollAt = command.LastSuccessfulPollAt.HasValue
            ? DateTime.SpecifyKind(command.LastSuccessfulPollAt.Value, DateTimeKind.Utc)
            : null;
        device.ApiBaseUrl = command.ApiBaseUrl;
        device.KitchenPrinter = command.KitchenPrinter;
        device.CashierPrinter = command.CashierPrinter;
        device.LastHeartbeatAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessWithData(true, "Heartbeat recorded.");
    }
}
