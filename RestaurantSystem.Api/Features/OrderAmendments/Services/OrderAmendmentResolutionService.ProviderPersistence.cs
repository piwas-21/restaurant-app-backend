using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task RecordProviderObservationAsync(
        Guid operationId, Guid legId, AmendmentRefundEvidence provider,
        Guid actorId, CancellationToken cancellationToken)
    {
        var attemptId = Guid.ParseExact(
            provider.Metadata[StripeOrderAmendmentRefundProvider.AttemptKey], "D");
        await using var scope = await LockResolutionSourceAsync(operationId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operationId, cancellationToken);
        var leg = await context.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == legId && value.OperationId == operationId,
                cancellationToken) ?? throw Unavailable();
        if (leg.Attempts.All(value => value.Id != attemptId))
            throw new ConflictException("The provider response does not match a persisted refund request.");

        var currentAttempt = leg.Attempts.OrderByDescending(value => value.Sequence).First();
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved
            || leg.State == OrderAmendmentRefundLegState.Succeeded
            || currentAttempt.Id != attemptId)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }

        var saved = await context.OrderAmendmentRefundEvidence
            .Where(value => value.RefundLegId == legId && value.RefundAttemptId == attemptId
                && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
            .OrderByDescending(value => value.Sequence).FirstOrDefaultAsync(cancellationToken);
        if (saved?.ProviderRefundId is not null)
        {
            if (saved.ProviderRefundId != provider.RefundId)
                throw new ConflictException("The provider changed refund identity for one durable request.");
            if (!CanTransition(saved.ProviderRefundStatus, provider.Status))
            {
                await scope.CommitAsync(cancellationToken);
                return;
            }
            if (saved.ProviderRefundStatus == provider.Status)
            {
                await scope.CommitAsync(cancellationToken);
                return;
            }
        }
        else if (saved is not null && saved.FailureCode != ProviderOutcomeUnknownFailureCode)
        {
            throw new ConflictException("The saved provider observation cannot be advanced safely.");
        }

        var sequence = await NextEvidenceSequenceAsync(legId, cancellationToken);
        var state = ProviderState(provider.Status);
        context.OrderAmendmentRefundEvidence.Add(NewEvidence(leg, currentAttempt, sequence,
            operation, new OrderAmendmentRefundObservation(
                OrderAmendmentRefundEvidenceKind.ProviderObservation, state, actorId,
                resolutionPolicy.UtcNow, null, provider, currentUser.GetAuditIdentifier())));
        leg.State = state;
        leg.ResolvedAt = state == OrderAmendmentRefundLegState.Succeeded
            ? resolutionPolicy.UtcNow : null;
        leg.FailureCode = ProviderFailureCode(state);
        await RefreshOperationStateAsync(operation, cancellationToken,
            recoverFromReconciliation: saved?.FailureCode == ProviderOutcomeUnknownFailureCode);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static bool CanTransition(string? previous, string current) => previous == current
        || (previous is "pending" or "requires_action"
            && current is "pending" or "requires_action" or "succeeded" or "failed" or "canceled");

    private async Task MarkProviderUnknownAsync(
        Guid operationId, Guid legId, Guid observedAttemptId,
        Guid actorId, CancellationToken cancellationToken)
    {
        await using var scope = await LockResolutionSourceAsync(operationId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operationId, cancellationToken);
        var leg = await context.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == legId && value.OperationId == operationId,
                cancellationToken);
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved
            || leg is null || leg.State == OrderAmendmentRefundLegState.Succeeded)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }
        var latestAttempt = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault();
        if (latestAttempt?.Id != observedAttemptId)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }
        var latestObservation = latestAttempt is null ? null
            : await context.OrderAmendmentRefundEvidence
                .Where(value => value.RefundLegId == legId
                    && value.RefundAttemptId == latestAttempt.Id
                    && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
                .OrderByDescending(value => value.Sequence).FirstOrDefaultAsync(cancellationToken);
        if (latestObservation is not null)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }
        var attempt = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault();
        var sequence = await NextEvidenceSequenceAsync(legId, cancellationToken);
        var evidence = NewEvidence(leg, attempt, sequence, operation,
            new OrderAmendmentRefundObservation(OrderAmendmentRefundEvidenceKind.ProviderObservation,
                OrderAmendmentRefundLegState.ReconciliationRequired, actorId,
                resolutionPolicy.UtcNow, null, null, currentUser.GetAuditIdentifier()));
        evidence.FailureCode = ProviderOutcomeUnknownFailureCode;
        context.OrderAmendmentRefundEvidence.Add(evidence);
        leg.State = OrderAmendmentRefundLegState.ReconciliationRequired;
        leg.FailureCode = ProviderOutcomeUnknownFailureCode;
        leg.ResolvedAt = null;
        operation.State = OrderAmendmentResolutionOperationState.ReconciliationRequired;
        operation.FailureCode = ProviderOutcomeUnknownFailureCode;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private async Task<OrderAmendmentRefundLeg> AppendRetryAttemptAsync(
        Guid operationId, Guid legId, Guid failedAttemptId, CancellationToken cancellationToken)
    {
        await using var scope = await LockResolutionSourceAsync(operationId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operationId, cancellationToken);
        var leg = await context.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == legId && value.OperationId == operationId,
                cancellationToken) ?? throw Unavailable();
        var latest = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault()
            ?? throw new ConflictException("The provider refund request identity is unavailable.");
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved)
        {
            await scope.CommitAsync(cancellationToken);
            return leg;
        }
        if (latest.Id != failedAttemptId)
        {
            await scope.CommitAsync(cancellationToken);
            return leg;
        }
        if (leg.State != OrderAmendmentRefundLegState.Failed
            || await context.OrderAmendmentRefundEvidence.AnyAsync(value => value.RefundLegId == legId
                && value.State == OrderAmendmentRefundLegState.Succeeded, cancellationToken))
            throw new ConflictException("Only a canonically failed refund can be retried.");

        var now = resolutionPolicy.UtcNow;
        var attemptId = Guid.NewGuid();
        var attempt = new OrderAmendmentRefundAttempt
        {
            Id = attemptId,
            RefundLegId = legId,
            Sequence = latest.Sequence + 1,
            IdempotencyKey = $"amendment-refund:{attemptId:N}",
            RequestedAt = now,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        leg.Attempts.Add(attempt);
        context.OrderAmendmentRefundAttempts.Add(attempt);
        leg.State = OrderAmendmentRefundLegState.Processing;
        leg.FailureCode = null;
        leg.ResolvedAt = null;
        var evidenceSequence = await NextEvidenceSequenceAsync(legId, cancellationToken);
        context.OrderAmendmentRefundEvidence.Add(NewEvidence(leg, attempt, evidenceSequence,
            operation, new OrderAmendmentRefundObservation(
                OrderAmendmentRefundEvidenceKind.ProviderRequest,
                OrderAmendmentRefundLegState.Processing, currentUser.UserId!.Value,
                now, null, null, currentUser.GetAuditIdentifier())));
        operation.State = OrderAmendmentResolutionOperationState.Processing;
        operation.FailureCode = null;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return leg;
    }

    private async Task<OrderAmendmentRefundEvidence?> ReadLatestLegObservationAsync(
        Guid legId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentRefundEvidence.AsNoTracking()
            .Where(value => value.RefundLegId == legId
                && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
            .OrderByDescending(value => value.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task SetProviderLegStateAsync(
        Guid operationId, Guid legId, Guid expectedAttemptId, OrderAmendmentRefundLegState state,
        CancellationToken cancellationToken,
        bool recoverFromReconciliation = false)
    {
        await using var scope = await LockResolutionSourceAsync(operationId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operationId, cancellationToken);
        var leg = await context.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == legId && value.OperationId == operationId,
                cancellationToken) ?? throw Unavailable();
        var currentAttempt = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault();
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved
            || currentAttempt?.Id != expectedAttemptId)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }
        if (state == OrderAmendmentRefundLegState.Succeeded)
        {
            var observation = await context.OrderAmendmentRefundEvidence
                .Where(value => value.RefundLegId == legId
                    && value.RefundAttemptId == expectedAttemptId
                    && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
                .OrderByDescending(value => value.Sequence).FirstOrDefaultAsync(cancellationToken);
            if (observation?.ProviderRefundId is null
                || observation.ProviderRefundStatus != "succeeded")
            {
                await scope.CommitAsync(cancellationToken);
                return;
            }
        }
        if (leg.State == OrderAmendmentRefundLegState.Succeeded)
        {
            if (state == OrderAmendmentRefundLegState.ReconciliationRequired)
            {
                operation.State = OrderAmendmentResolutionOperationState.ReconciliationRequired;
                operation.FailureCode = "provider_refund_requires_reconciliation";
                operation.UpdatedAt = resolutionPolicy.UtcNow;
                operation.UpdatedBy = currentUser.GetAuditIdentifier();
                await context.SaveChangesAsync(cancellationToken);
            }
            else if (state == OrderAmendmentRefundLegState.Succeeded && recoverFromReconciliation
                     && operation.State == OrderAmendmentResolutionOperationState.ReconciliationRequired)
            {
                await RefreshOperationStateAsync(operation, cancellationToken,
                    recoverFromReconciliation: true);
                operation.UpdatedAt = resolutionPolicy.UtcNow;
                operation.UpdatedBy = currentUser.GetAuditIdentifier();
                await context.SaveChangesAsync(cancellationToken);
            }
            await scope.CommitAsync(cancellationToken);
            return;
        }
        leg.State = state;
        leg.ResolvedAt = state == OrderAmendmentRefundLegState.Succeeded
            ? resolutionPolicy.UtcNow : null;
        leg.FailureCode = ProviderFailureCode(state);
        await RefreshOperationStateAsync(operation, cancellationToken, recoverFromReconciliation);
        operation.UpdatedAt = resolutionPolicy.UtcNow;
        operation.UpdatedBy = currentUser.GetAuditIdentifier();
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private async Task<int> NextEvidenceSequenceAsync(Guid legId, CancellationToken cancellationToken)
    {
        var maximum = await context.OrderAmendmentRefundEvidence.AsNoTracking()
            .Where(value => value.RefundLegId == legId)
            .Select(value => (int?)value.Sequence).MaxAsync(cancellationToken);
        return checked((maximum ?? 0) + 1);
    }

    private async Task<OrderAccountMutationScope> LockResolutionSourceAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        var orderId = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId).Select(value => (Guid?)value.SourceOrderId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        return await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
    }

    private static OrderAmendmentRefundLegState ProviderState(string status) => status switch
    {
        "succeeded" => OrderAmendmentRefundLegState.Succeeded,
        "pending" or "requires_action" => OrderAmendmentRefundLegState.Pending,
        "failed" or "canceled" => OrderAmendmentRefundLegState.Failed,
        _ => OrderAmendmentRefundLegState.ReconciliationRequired
    };

    private static string? ProviderFailureCode(OrderAmendmentRefundLegState state) => state switch
    {
        OrderAmendmentRefundLegState.Pending => "provider_refund_pending",
        OrderAmendmentRefundLegState.Failed => "provider_refund_failed",
        OrderAmendmentRefundLegState.ReconciliationRequired => "provider_refund_requires_reconciliation",
        _ => null
    };
}
