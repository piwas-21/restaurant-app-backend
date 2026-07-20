using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordDeviceEventsCommand;

/// <summary>
/// Ingests a batch of append-only diagnostic events from one printer-app. De-duplicated by
/// <c>(DeviceId, ClientEventId)</c> so a <b>sequential</b> retry never double-inserts — the outbox
/// sends per-device single-flight, so concurrent same-device batches don't occur. <see cref="DeviceId"/>
/// comes from the <c>X-Device-Id</c> header (the controller injects it), never the body.
/// </summary>
public record RecordDeviceEventsCommand(
    [property: JsonIgnore] string DeviceId,
    List<DeviceEventDto> Events
) : ICommand<ApiResponse<bool>>;

public class RecordDeviceEventsCommandHandler
    : ICommandHandler<RecordDeviceEventsCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RecordDeviceEventsCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<bool>> Handle(
        RecordDeviceEventsCommand command, CancellationToken cancellationToken)
    {
        // Distinct within the batch, then drop any already persisted — idempotent under sequential
        // retry (the outbox is single-flight per device, so no concurrent same-device batch races).
        var byClientId = command.Events
            .GroupBy(e => e.ClientEventId)
            .ToDictionary(g => g.Key, g => g.First());

        var clientIds = byClientId.Keys.ToList();
        var known = await _context.DeviceEvents
            .Where(e => e.DeviceId == command.DeviceId && clientIds.Contains(e.ClientEventId))
            .Select(e => e.ClientEventId)
            .ToListAsync(cancellationToken);

        foreach (var clientId in known)
            byClientId.Remove(clientId);

        var auditId = _currentUserService.GetAuditIdentifier();
        foreach (var dto in byClientId.Values)
        {
            _context.DeviceEvents.Add(new DeviceEvent
            {
                DeviceId = command.DeviceId,
                ClientEventId = dto.ClientEventId,
                // Client instants are UTC; the column is `timestamptz` and Npgsql rejects a
                // non-UTC Kind. Relabel rather than convert (see the print-ack handler).
                OccurredAt = DateTime.SpecifyKind(dto.OccurredAt, DateTimeKind.Utc),
                Level = dto.Level,
                Code = dto.Code,
                Message = dto.Message,
                Context = dto.Context,
                CreatedBy = auditId,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessWithData(true, "Device events recorded.");
    }
}
