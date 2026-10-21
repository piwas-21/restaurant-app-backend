using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

public sealed partial class CashierPrintQueue(ApplicationDbContext context,
    ITableBillAssembler bills, IOrderMappingService mapping, ICurrentStaffTicketReader tickets, ICurrentUserService currentUser, IOptions<CashierPrintSettings> settings) : ICashierPrintQueue
{
    private readonly CashierPrintSettings _settings = settings.Value;

    private async Task<CashierPrintJobDto> QueueOnceAsync(QueueCashierPrintRequest request,
        bool automatic, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || request.ExpectedVersion < 1
            || request.OrderId.HasValue == request.ServiceSessionId.HasValue)
            throw new BadRequestException("Select exactly one order or table visit with its current version.");
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken) : null;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({request.OperationId.ToString()}, 0))",
            cancellationToken);
        var existing = await context.CashierPrintJobs.SingleOrDefaultAsync(
            job => job.Id == request.OperationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OrderId != request.OrderId || existing.ServiceSessionId != request.ServiceSessionId
                || existing.IsAutomatic != automatic)
                throw new ConflictException("This print request belongs to a different bill.");
            return ToDto(existing);
        }

        await using var orderLock = request.OrderId is Guid orderId
            ? await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken) : null;
        var receipt = await BuildReceiptAsync(request, cancellationToken);
        var job = new CashierPrintJob
        {
            Id = request.OperationId,
            OrderId = request.OrderId,
            ServiceSessionId = request.ServiceSessionId,
            IsAutomatic = automatic,
            SnapshotJson = JsonSerializer.Serialize(receipt, CashierReceiptSnapshot.Json),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetAuditIdentifier(),
        };
        context.CashierPrintJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return ToDto(job);
    }

    public async Task<CashierPrintJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToDto(await context.CashierPrintJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == jobId,
            cancellationToken) ?? throw new NotFoundException("Print request not found."));

    private CashierPrintJobDto ToDto(CashierPrintJob job, bool includeReceipt = false) =>
        new(job.Id, job.Status == "Queued" && job.CreatedAt < DateTime.UtcNow.AddDays(-_settings.QueueLifetimeDays)
            ? "Expired" : job.Status, job.IsAutomatic, job.CreatedAt, includeReceipt
            ? JsonSerializer.Deserialize<RestaurantSystem.Api.Features.Orders.Dtos.OrderDto>(
                job.SnapshotJson, CashierReceiptSnapshot.Json) : null);
}
