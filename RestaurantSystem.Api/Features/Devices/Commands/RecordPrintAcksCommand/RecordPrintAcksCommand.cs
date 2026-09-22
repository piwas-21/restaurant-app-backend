using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;

/// <summary>
/// Ingests a batch of order and additive print-job outcomes from one printer-app. Legacy acks
/// upsert by <c>(OrderId, DeviceId, Target)</c>. New jobs upsert by
/// <c>(DeviceId, JobId, Revision, Target)</c>, so a note update never overwrites the original
/// order receipt. <see cref="DeviceId"/> comes from the <c>X-Device-Id</c> header (the controller
/// injects it), never the body.
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
    private readonly IOrderRoutingService _routing;

    public RecordPrintAcksCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService,
        IOrderRoutingService routing)
    {
        _context = context;
        _currentUserService = currentUserService;
        _routing = routing;
    }

    public async Task<ApiResponse<bool>> Handle(
        RecordPrintAcksCommand command, CancellationToken cancellationToken)
        => await HandleCoreAsync(command, true, cancellationToken);

    private async Task<ApiResponse<bool>> HandleCoreAsync(
        RecordPrintAcksCommand command, bool retryOnRace, CancellationToken cancellationToken)
    {
        try
        {
            var orderIds = command.Acks.Select(a => a.OrderId).ToHashSet();
            var jobIds = command.Acks
                .Where(a => a.JobId.HasValue)
                .Select(a => a.JobId!.Value)
                .ToHashSet();

            // Include both natural-key families. A job can legitimately share an OrderId and Target
            // with the legacy receipt, while a malformed retry may identify a known job with another
            // order id; loading by both sets lets us reject that collision without adding a row.
            var existing = await _context.DeviceOrderReceipts
                .Where(r => r.DeviceId == command.DeviceId
                    && (orderIds.Contains(r.OrderId)
                        || (r.JobId.HasValue && jobIds.Contains(r.JobId.Value))))
                .ToListAsync(cancellationToken);

            var byLegacyKey = existing
                .Where(r => !r.JobId.HasValue)
                .ToDictionary(r => (r.OrderId, r.Target));
            var jobs = existing
                .Where(r => r.JobId.HasValue && r.Revision.HasValue)
                .ToList();
            var byJobKey = jobs
                .ToDictionary(r => (r.JobId!.Value, r.Revision!.Value, r.Target));
            var byJobOrder = jobs
                .GroupBy(r => r.JobId!.Value)
                .ToDictionary(group => group.Key, group => group.First().OrderId);
            var auditId = _currentUserService.GetAuditIdentifier();

            foreach (var originalAck in command.Acks)
            {
                var ack = PrintAckNormalizer.NormalizeOrderAcknowledgement(originalAck);
                string? error = null;
                var receipt = ack.JobId.HasValue
                    ? ResolveJobReceipt(ack, command.DeviceId, auditId, byJobKey, byJobOrder, out error)
                    : ResolveLegacyReceipt(ack, command.DeviceId, auditId, byLegacyKey);
                if (receipt is null)
                {
                    return ApiResponse<bool>.Failure(error!);
                }

                ApplyAcknowledgement(receipt, ack);
                await _routing.ApplyAcknowledgementAsync(command.DeviceId, ack, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return ApiResponse<bool>.SuccessWithData(true, "Print acknowledgements recorded.");
        }
        catch (DbUpdateConcurrencyException) when (retryOnRace)
        {
            _context.ChangeTracker.Clear();
            return await HandleCoreAsync(command, false, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (retryOnRace && IsReceiptUniqueViolation(exception))
        {
            // Two device flushes may insert the same receipt simultaneously. The unique natural
            // key makes one winner authoritative; reload the batch once so the loser becomes the
            // same successful idempotent operation instead of a 500.
            _context.ChangeTracker.Clear();
            return await HandleCoreAsync(command, false, cancellationToken);
        }
    }

    private DeviceOrderReceipt? ResolveJobReceipt(
        PrintAckDto ack,
        string deviceId,
        string auditId,
        Dictionary<(Guid JobId, int Revision, DevicePrintTarget Target), DeviceOrderReceipt> byJobKey,
        Dictionary<Guid, Guid> byJobOrder,
        out string? error)
    {
        error = null;
        if (!ack.Revision.HasValue || !ack.JobType.HasValue)
        {
            error = "A print job acknowledgement requires revision and job type.";
            return null;
        }

        var jobId = ack.JobId!.Value;
        if (byJobOrder.TryGetValue(jobId, out var boundOrderId) && boundOrderId != ack.OrderId)
        {
            error = "This print job is already bound to a different order.";
            return null;
        }

        byJobOrder[jobId] = ack.OrderId;
        var jobKey = (jobId, ack.Revision.Value, ack.Target);
        if (byJobKey.TryGetValue(jobKey, out var receipt))
        {
            return receipt;
        }

        receipt = CreateReceipt(ack, deviceId, auditId);
        byJobKey[jobKey] = receipt;
        return receipt;
    }

    private DeviceOrderReceipt ResolveLegacyReceipt(
        PrintAckDto ack,
        string deviceId,
        string auditId,
        Dictionary<(Guid OrderId, DevicePrintTarget Target), DeviceOrderReceipt> byLegacyKey)
    {
        var legacyKey = (ack.OrderId, ack.Target);
        if (byLegacyKey.TryGetValue(legacyKey, out var receipt))
        {
            return receipt;
        }

        receipt = CreateReceipt(ack, deviceId, auditId);
        byLegacyKey[legacyKey] = receipt;
        return receipt;
    }

    private DeviceOrderReceipt CreateReceipt(PrintAckDto ack, string deviceId, string auditId)
    {
        var receipt = new DeviceOrderReceipt
        {
            DeviceId = deviceId,
            OrderId = ack.OrderId,
            JobId = ack.JobId,
            Revision = ack.Revision,
            JobType = ack.JobType,
            Target = ack.Target,
            CreatedBy = auditId,
        };
        _context.DeviceOrderReceipts.Add(receipt);
        return receipt;
    }

    private static void ApplyAcknowledgement(DeviceOrderReceipt receipt, PrintAckDto ack)
    {
        receipt.Status = ack.Status;
        receipt.FailureReason = ack.FailureReason;
        receipt.Copies = ack.Copies;
        receipt.ReceivedAt = AsUtc(ack.ReceivedAt);
        receipt.PrintedAt = ack.PrintedAt.HasValue ? AsUtc(ack.PrintedAt.Value) : null;
    }

    // Client instants are UTC; the columns are `timestamptz` and Npgsql rejects a non-UTC Kind
    // (a zoneless JSON timestamp deserialises to Kind=Unspecified). Relabel rather than convert —
    // matching the heartbeat handler and Groups/UserGroupService's client-DateTime handling.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool IsReceiptUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.Contains("DeviceOrderReceipts", StringComparison.OrdinalIgnoreCase) == true
            || pg.ConstraintName?.Contains("device_order_receipts", StringComparison.OrdinalIgnoreCase) == true);
}
