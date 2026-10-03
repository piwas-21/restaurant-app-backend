using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCheckoutAmendmentRefundProof
{
    internal static async Task<long> ValidateAsync(ApplicationDbContext context,
        AccountCheckoutJournal journal, AccountCheckoutCanonicalEvidence evidence,
        CancellationToken cancellationToken)
    {
        if (evidence.Charge is not { CapturedMinor: > 0 } charge)
        {
            if (evidence.Refunds.Count != 0 || journal.ProviderRefundedMinor != 0)
                throw NeedsReconciliation();
            return 0;
        }

        var legs = await context.OrderAmendmentRefundLegs.AsNoTracking()
            .Where(value => value.AccountPaymentAttemptId == journal.AttemptId)
            .Include(value => value.Attempts).ToListAsync(cancellationToken);
        var operationIds = legs.Select(value => value.OperationId).Distinct().ToArray();
        var operations = operationIds.Length == 0 ? []
            : await context.OrderAmendmentResolutionOperations.AsNoTracking()
                .Where(value => operationIds.Contains(value.Id)).Include(value => value.Legs)
                .ToListAsync(cancellationToken);
        var operationById = operations.ToDictionary(value => value.Id);
        var legIds = legs.Select(value => value.Id).ToArray();
        var stored = legIds.Length == 0 ? []
            : await context.OrderAmendmentRefundEvidence.AsNoTracking()
                .Where(value => legIds.Contains(value.RefundLegId)).ToListAsync(cancellationToken);
        ValidateLegStates(legs, operationById, stored, journal, charge);
        var operationByLeg = legs.ToDictionary(value => value.Id, value => value.OperationId);
        var legByAttempt = legs.SelectMany(leg => leg.Attempts.Select(attempt =>
                (AttemptId: attempt.Id, LegId: leg.Id)))
            .ToDictionary(value => value.AttemptId, value => value.LegId);
        var verifiedRefundedMinor = OrderAmendmentRefundProviderProof.RequireCanonicalHistory(
            stored, evidence.Refunds, new AmendmentRefundProviderContext(
                journal.ProviderAccountId, journal.ProviderLiveMode), charge.Id,
            journal.ProviderIntentId ?? string.Empty, journal.Currency,
            operationByLeg, legByAttempt);
        if (charge.RefundedMinor != verifiedRefundedMinor
            || verifiedRefundedMinor > journal.ProviderCapturedMinor
            || journal.ProviderRefundedMinor > verifiedRefundedMinor)
            throw NeedsReconciliation();
        return verifiedRefundedMinor;
    }

    private static void ValidateLegStates(
        IReadOnlyList<OrderAmendmentRefundLeg> legs,
        Dictionary<Guid, OrderAmendmentResolutionOperation> operationById,
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        AccountCheckoutJournal journal,
        AccountStripeCharge charge)
    {
        foreach (var leg in legs)
        {
            if (!operationById.TryGetValue(leg.OperationId, out var operation)
                || leg.ProviderChargeId != charge.Id || leg.ProviderIntentId != journal.ProviderIntentId
                || leg.ProviderAccountId != journal.ProviderAccountId
                || leg.ProviderLiveMode != journal.ProviderLiveMode
                || leg.Currency != journal.Currency || leg.Custody != OrderAmendmentRefundCustody.StripeDirect)
                throw NeedsReconciliation();

            if (operation.State == OrderAmendmentResolutionOperationState.Resolved)
            {
                if (leg.State != OrderAmendmentRefundLegState.Succeeded)
                    throw NeedsReconciliation();
                OrderAmendmentRefundProviderProof.RequireStoredAttemptHistory(
                    leg, operation, stored.Where(value => value.RefundLegId == leg.Id).ToArray(), requireSuccess: true);
                continue;
            }

            if (operation.State is not (OrderAmendmentResolutionOperationState.Processing
                    or OrderAmendmentResolutionOperationState.ReconciliationRequired)
                || leg.State is not (OrderAmendmentRefundLegState.Processing
                    or OrderAmendmentRefundLegState.Pending or OrderAmendmentRefundLegState.Failed
                    or OrderAmendmentRefundLegState.Succeeded or OrderAmendmentRefundLegState.ReconciliationRequired)
                || operation.ActorRole != UserRole.Admin.ToString()
                || operation.Currency != journal.Currency
                || operation.Legs.Sum(value => value.AmountMinor) != operation.RefundMinor)
                throw NeedsReconciliation();
            OrderAmendmentRefundProviderProof.RequireStoredAttemptHistory(
                leg, operation, stored.Where(value => value.RefundLegId == leg.Id).ToArray(), requireSuccess: false);
        }
    }

    private static ConflictException NeedsReconciliation() =>
        new("The provider refund history is not fully bound to this account contribution.");
}
