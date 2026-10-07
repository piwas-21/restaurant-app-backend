using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyEvidenceReader
{
    internal static async Task<OrderAmendmentLoyaltyEvidence> ReadAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var snapshot = await context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == orderId, cancellationToken);
        var units = await context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.OrderItemId).ThenBy(value => value.UnitOrdinal)
            .ToListAsync(cancellationToken);
        var ownerLinks = await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.Slot).ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);
        var witnesses = await context.OrderBillingAwardWitnesses.AsNoTracking()
            .Where(value => value.OrderId == orderId).Take(2).ToListAsync(cancellationToken);
        var coverage = await context.OrderBillingAwardUnitCoverages.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.SnapshotUnitId).ToListAsync(cancellationToken);
        var suppressions = await context.OrderBillingUnitAwardSuppressions.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.SnapshotUnitId).ToListAsync(cancellationToken);
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId && value.State == Domain.Common.Enums.OrderAmendmentState.Committed)
            .OrderBy(value => value.CommittedAt).ThenBy(value => value.Id).ToListAsync(cancellationToken);
        var transactions = await context.FidelityPointsTransactions.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        var compensations = await context.OrderAmendmentLoyaltyCompensations.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId)
            .OrderBy(value => value.AmendmentId).ThenBy(value => value.OriginalTransactionId)
            .ThenBy(value => value.Kind).ToListAsync(cancellationToken);
        var compensationIds = compensations.Select(value => value.Id).ToArray();
        var priorUnits = compensationIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyCompensationUnits.AsNoTracking()
                .Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.SnapshotUnitId).ThenBy(value => value.Kind)
                .ToListAsync(cancellationToken);
        var priorPostings = compensationIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
                .Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.CompensationId).ToListAsync(cancellationToken);
        var reservations = compensationIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyReservations.AsNoTracking()
                .Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.CompensationId).ToListAsync(cancellationToken);
        var operations = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId)
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        if (witnesses.Count > 1)
            throw new RestaurantSystem.Api.Common.Exceptions.ConflictException(
                "The source order has duplicate immutable loyalty award witnesses.");

        return new(snapshot, units, ownerLinks, witnesses.SingleOrDefault(), coverage,
            suppressions, amendments, transactions, compensations, priorUnits,
            priorPostings, reservations, operations);
    }
}
