using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal sealed class KitchenBoardReceiptBatch : IAsyncDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IDbContextTransaction? _transaction;
    private readonly HashSet<(Guid OrderId, Guid JobId, DevicePrintTarget Target)> _changed = [];

    private KitchenBoardReceiptBatch(ApplicationDbContext context, IDbContextTransaction? transaction)
    {
        _context = context;
        _transaction = transaction;
    }

    internal static async Task<KitchenBoardReceiptBatch> BeginAsync(
        ApplicationDbContext context,
        ITenantFeatures? features,
        IReadOnlyCollection<PrintAckDto> acknowledgements,
        CancellationToken cancellationToken)
    {
        var updatesBoard = KitchenBoardFeaturePolicy.IsEnabled(features)
            && acknowledgements.Any(ack => ack.JobId.HasValue
                && ack.JobType == DevicePrintJobType.Update);
        if (!updatesBoard)
            return new KitchenBoardReceiptBatch(context, null);

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await KitchenBoardSequenceWriter.LockReceiptBatchAsync(
                context, acknowledgements.Select(ack => ack.OrderId), cancellationToken);
            return new KitchenBoardReceiptBatch(context, transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    internal void TrackPotentialCorrectionChange(DeviceOrderReceipt receipt, PrintAckDto acknowledgement)
    {
        if (_transaction is null || acknowledgement.JobType != DevicePrintJobType.Update
            || acknowledgement.Revision != PrinterUpdateRevisions.Original)
            return;

        var wasPrinted = receipt.Status == DevicePrintStatus.Printed;
        var willBePrinted = acknowledgement.Status == DevicePrintStatus.Printed;
        if (wasPrinted != willBePrinted)
            _changed.Add((acknowledgement.OrderId, acknowledgement.JobId!.Value, acknowledgement.Target));
    }

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
            return;

        foreach (var receipt in _changed)
        {
            await KitchenBoardSequenceWriter.TouchCorrectionAsync(
                _context, receipt.OrderId, receipt.JobId, receipt.Target, cancellationToken);
        }

        await _transaction.CommitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
