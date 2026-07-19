using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;

/// <summary>
/// Ingests a batch of order print outcomes from one printer-app. Each ack upserts by
/// <c>(OrderId, DeviceId, Target)</c>, so a <b>sequential</b> retry is idempotent — the outbox
/// sends per-device single-flight, so concurrent same-device batches don't occur. <see cref="DeviceId"/>
/// comes from the <c>X-Device-Id</c> header (the controller injects it), never the body.
/// </summary>
public record RecordPrintAcksCommand(
    [property: JsonIgnore] string DeviceId,
    List<PrintAckDto> Acks
) : ICommand<ApiResponse<bool>>;

public class RecordPrintAcksCommandHandler
    : ICommandHandler<RecordPrintAcksCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RecordPrintAcksCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<bool>> Handle(
        RecordPrintAcksCommand command, CancellationToken cancellationToken)
    {
        var orderIds = command.Acks.Select(a => a.OrderId).ToHashSet();
        var existing = await _context.DeviceOrderReceipts
            .Where(r => r.DeviceId == command.DeviceId && orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        var auditId = _currentUserService.GetAuditIdentifier();
        foreach (var ack in command.Acks)
        {
            var receipt = existing.FirstOrDefault(r =>
                r.OrderId == ack.OrderId && r.Target == ack.Target);

            if (receipt is null)
            {
                receipt = new DeviceOrderReceipt
                {
                    DeviceId = command.DeviceId,
                    OrderId = ack.OrderId,
                    Target = ack.Target,
                    CreatedBy = auditId,
                };
                _context.DeviceOrderReceipts.Add(receipt);
                existing.Add(receipt);
            }

            receipt.Status = ack.Status;
            receipt.FailureReason = ack.FailureReason;
            receipt.Copies = ack.Copies;
            receipt.ReceivedAt = AsUtc(ack.ReceivedAt);
            receipt.PrintedAt = ack.PrintedAt.HasValue ? AsUtc(ack.PrintedAt.Value) : null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessWithData(true, "Print acknowledgements recorded.");
    }

    // Client instants are UTC; the columns are `timestamptz` and Npgsql rejects a non-UTC Kind
    // (a zoneless JSON timestamp deserialises to Kind=Unspecified). Relabel rather than convert —
    // matching the heartbeat handler and Groups/UserGroupService's client-DateTime handling.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
