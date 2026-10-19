using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

public sealed partial class CashierPrintQueue
{
    private const int CapabilityFreshnessMinutes = 5;
    private const int QueueLifetimeDays = 1;
    public async Task<CashierPrintJobDto?> ClaimAsync(string deviceId, CancellationToken cancellationToken, bool allowAutomatic = true)
    {
        RequireDeviceId(deviceId);
        var cutoff = DateTime.UtcNow.AddMinutes(-CapabilityFreshnessMinutes);
        var capability = await context.PrinterDeviceTargetCapabilities.AsNoTracking()
            .SingleOrDefaultAsync(capability => capability.DeviceId == deviceId
                && capability.Target == DevicePrintTarget.Cashier && capability.IsSupported
                && capability.IsConfigured && capability.ReportedAt >= cutoff, cancellationToken);
        if (capability is null) return null;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var expiry = DateTime.UtcNow.AddDays(-QueueLifetimeDays);
        var autoEnabled = capability.AutoPrintEnabled && allowAutomatic;
        // The claim is committed BEFORE bytes leave the server. A lost response/crash holds this
        // job as Claimed; no lease expiry can silently print an ambiguous bill a second time.
        var jobs = await context.CashierPrintJobs.FromSqlInterpolated($"""
            SELECT * FROM cashier_print_jobs
            WHERE status = 'Queued' AND created_at >= {expiry}
              AND (order_id IS NULL OR EXISTS (SELECT 1 FROM orders WHERE orders.id = cashier_print_jobs.order_id AND NOT orders.is_deleted))
              AND (NOT is_automatic OR {autoEnabled})
            ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var job = jobs.SingleOrDefault();
        if (job is null) return null;
        job.Status = "Claimed"; job.DeviceId = deviceId; job.ClaimedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = ToDto(job, includeReceipt: true);
        if (job.OrderId.HasValue && result.Receipt is { } receipt)
        {
            // Contact remains on its original erasure-aware order, never in the durable print snapshot.
            var source = await context.Orders.AsNoTracking().IncludeOrderLineGraph().AsSplitQuery()
                .Include(order => order.Payments).Include(order => order.DeliveryAddress)
                .SingleOrDefaultAsync(order => order.Id == job.OrderId, cancellationToken);
            if (source is not null)
            {
                var current = await mapping.MapToOrderDtoAsync(source, cancellationToken);
                receipt.CustomerName = current.CustomerName; receipt.CustomerEmail = current.CustomerEmail;
                receipt.CustomerPhone = current.CustomerPhone; receipt.DeliveryAddress = current.DeliveryAddress;
            }
        }
        return result;
    }

    public async Task CompleteAsync(Guid jobId, string deviceId, string status,
        CancellationToken cancellationToken)
    {
        RequireDeviceId(deviceId);
        if (status is not ("Sent" or "Failed" or "Unknown"))
            throw new BadRequestException("Invalid printer outcome.");
        var affected = await context.CashierPrintJobs
            .Where(job => job.Id == jobId && job.DeviceId == deviceId
                && (job.Status == "Claimed" || job.Status == status))
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, status)
                .SetProperty(job => job.CompletedAt, DateTime.UtcNow), cancellationToken);
        if (affected == 0) throw new ConflictException("This job is not claimed by this device.");
    }

    private static void RequireDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64)
            throw new BadRequestException("A valid device identity is required.");
    }
}
