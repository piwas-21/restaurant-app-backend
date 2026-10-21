using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

public sealed partial class CashierPrintQueue
{
    private const int MaximumSnapshotAttempts = 3;

    public async Task<CashierPrintJobDto> QueueAsync(QueueCashierPrintRequest request,
        bool automatic, CancellationToken cancellationToken)
    {
        // A visit close owns its existing transaction and session lock. A standalone request owns
        // a repeatable snapshot; an advisory-lock waiter may need a fresh snapshot to see a replay.
        var ownsTransaction = context.Database.CurrentTransaction is null;
        for (var attempt = 0; attempt < MaximumSnapshotAttempts; attempt++)
        {
            try
            {
                return await QueueOnceAsync(request, automatic, cancellationToken);
            }
            catch (Exception exception) when (ownsTransaction && IsSnapshotRace(exception))
            {
                var tracked = context.CashierPrintJobs.Local.SingleOrDefault(job => job.Id == request.OperationId);
                if (tracked is not null) context.Entry(tracked).State = EntityState.Detached;
            }
        }
        throw new ConflictException("The bill changed while printing. Refresh before printing again.");
    }

    private static bool IsSnapshotRace(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => true,
        DbUpdateException
        {
            InnerException: PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation }
        } => true,
        _ => false,
    };
}
