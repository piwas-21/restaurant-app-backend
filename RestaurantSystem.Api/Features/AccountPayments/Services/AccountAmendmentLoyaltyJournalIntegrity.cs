using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Checks loyalty posting receipts for settled refund operations before reusing them as history.</summary>
internal static class AccountAmendmentLoyaltyJournalIntegrity
{
    internal static async Task ValidateResolvedOperationsAsync(
        ApplicationDbContext context,
        IReadOnlyList<OrderAmendmentResolutionOperation> operations,
        IReadOnlyList<OrderAmendment> amendments,
        CancellationToken cancellationToken)
    {
        var resolved = operations.Where(value => value.State == OrderAmendmentResolutionOperationState.Resolved)
            .ToArray();
        if (resolved.Length == 0)
            return;

        var operationIds = resolved.Select(value => value.Id).ToArray();
        var compensations = await context.OrderAmendmentLoyaltyCompensations.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId)).ToListAsync(cancellationToken);
        var compensationIds = compensations.Select(value => value.Id).ToArray();
        var units = compensationIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyCompensationUnits.AsNoTracking()
                .Where(value => compensationIds.Contains(value.CompensationId)).ToListAsync(cancellationToken);
        var reservations = await context.OrderAmendmentLoyaltyReservations.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId)).ToListAsync(cancellationToken);
        var postings = compensationIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
                .Where(value => compensationIds.Contains(value.CompensationId)).ToListAsync(cancellationToken);
        var amendmentById = amendments.ToDictionary(value => value.Id);

        foreach (var operation in resolved)
        {
            if (!amendmentById.ContainsKey(operation.AmendmentId))
                throw ReconciliationRequired();
            OrderAmendmentResolutionSnapshot snapshot;
            OrderAmendmentResolutionResultDto result;
            try
            {
                snapshot = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operation.SnapshotJson);
                result = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(operation.ResultJson
                    ?? throw ReconciliationRequired());
            }
            catch (JsonException exception)
            {
                throw new ConflictException("The settled loyalty journal snapshot requires reconciliation.", exception);
            }

            ValidatePlanFingerprint(operation, snapshot);

            var operationCompensations = compensations.Where(value => value.OperationId == operation.Id).ToArray();
            var operationUnits = units.Where(value => operationCompensations.Any(header =>
                header.Id == value.CompensationId)).ToArray();
            var operationReservations = reservations.Where(value => value.OperationId == operation.Id).ToArray();
            var operationPostings = postings.Where(value => operationCompensations.Any(header =>
                header.Id == value.CompensationId)).ToArray();
            OrderAmendmentLoyaltyResultFactory.ValidateSettledJournalEvidence(operation,
                snapshot.LoyaltyPlan, operationCompensations, operationUnits, operationReservations,
                operationPostings, result.Loyalty);
        }
    }

    private static void ValidatePlanFingerprint(
        OrderAmendmentResolutionOperation operation, OrderAmendmentResolutionSnapshot snapshot)
    {
        if (snapshot.Currency != operation.Currency || snapshot.CreditMinor != operation.CreditMinor
            || snapshot.RefundMinor != operation.RefundMinor
            || snapshot.UnpaidWaivedMinor != operation.UnpaidWaivedMinor
            || snapshot.RequestHash != operation.RequestHash
            || snapshot.LoyaltyPlanVersion is not null
                && snapshot.LoyaltyPlanVersion != OrderAmendmentLoyaltyPlanFingerprint.Version
            || snapshot.LoyaltyPlan?.SnapshotId.HasValue == true
                && snapshot.LoyaltyPlanVersion != OrderAmendmentLoyaltyPlanFingerprint.Version
            || !string.Equals(snapshot.PlanFingerprint,
                OrderAmendmentResolutionPlanFingerprint.Create(operation, operation.Legs.ToArray()),
                StringComparison.Ordinal))
            throw ReconciliationRequired();
    }

    private static ConflictException ReconciliationRequired() =>
        new("The settled loyalty journal requires reconciliation before reusing its refund history.");
}
