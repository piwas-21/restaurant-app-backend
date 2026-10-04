using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private const string ProviderOutcomeUnknownFailureCode = "provider_outcome_unknown";

    private async Task<OrderAmendmentResolutionResultDto> ProcessAndReadAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        var operation = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Include(value => value.Legs).ThenInclude(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken) ?? throw Unavailable();
        if (operation.ActorUserId != actorId)
            throw Unavailable();
        if (operation.State != OrderAmendmentResolutionOperationState.Resolved)
        {
            foreach (var leg in operation.Legs.Where(value =>
                         value.Custody == OrderAmendmentRefundCustody.StripeDirect))
                await ProcessStripeLegSafelyAsync(operationId, leg, actorId, cancellationToken);
            await finalizer.TryFinalizeAsync(operationId, actorId, cancellationToken);
        }
        return await ReadResultAsync(operationId, cancellationToken);
    }

    private async Task ProcessStripeLegSafelyAsync(
        Guid operationId, OrderAmendmentRefundLeg preloadedLeg,
        Guid actorId, CancellationToken cancellationToken)
    {
        var observedAttemptId = preloadedLeg.Attempts.OrderByDescending(value => value.Sequence)
            .FirstOrDefault()?.Id ?? Guid.Empty;
        PreparedProviderRefund? prepared;
        try
        {
            prepared = await PrepareProviderRefundAsync(operationId, preloadedLeg, actorId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            resolutionPolicy.WarnProviderFailure(exception, preloadedLeg.Id);
            await MarkProviderUnknownAsync(operationId, preloadedLeg.Id, observedAttemptId, actorId, cancellationToken);
            return;
        }

        if (prepared is not null)
            await SubmitProviderRefundSafelyAsync(operationId, prepared.Leg, prepared.Attempt,
                prepared.ProviderContext, actorId, cancellationToken);
    }

    private async Task<PreparedProviderRefund?> PrepareProviderRefundAsync(
        Guid operationId, OrderAmendmentRefundLeg leg,
        Guid actorId, CancellationToken cancellationToken)
    {
        // The actor-scoped parent query preloaded this leg and its immutable attempt identities, avoiding
        // a duplicate initial per-leg read. The snapshot cannot authorize retries or writes: those paths
        // reload current state under the source lock, and provider evidence stays fresh per call.
        if (leg.Custody != OrderAmendmentRefundCustody.StripeDirect)
            return null;
        if (leg.State == OrderAmendmentRefundLegState.Succeeded
            && await VerifyProviderLegAsync(operationId, leg.Id, cancellationToken))
            return null;

        var attempt = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault()
            ?? throw new ConflictException("The provider refund request identity is unavailable.");
        var providerSnapshot = await ReadVerifiedProviderRefundsAsync(
            operationId, leg, attempt.Id, cancellationToken);

        var current = FindCurrentRefund(providerSnapshot.Refunds, operationId, leg.Id, attempt.Id);
        if (current is not null && await HandleCurrentRefundAsync(
                operationId, leg.Id, current, actorId, cancellationToken))
            return null;

        (leg, attempt) = await PrepareProviderAttemptAsync(operationId, leg, attempt, cancellationToken);
        return new PreparedProviderRefund(leg, attempt, providerSnapshot.Context);
    }

    private async Task SubmitProviderRefundSafelyAsync(
        Guid operationId,
        OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundAttempt attempt,
        AmendmentRefundProviderContext providerContext,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = BuildProviderRequest(leg, operationId, attempt);
            var response = await refundProvider.CreateAsync(request, cancellationToken);
            OrderAmendmentRefundProviderProof.ValidateIdentity(response, providerContext,
                leg.ProviderChargeId!, leg.ProviderIntentId!, leg.Currency);
            ValidateCurrentRefund(response, operationId, leg.Id, attempt.Id, leg.AmountMinor);
            await RecordProviderObservationAsync(operationId, leg.Id, response, actorId, cancellationToken);
            if (response.Status == "succeeded")
                await VerifyProviderLegAsync(operationId, leg.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            resolutionPolicy.WarnProviderFailure(exception, leg.Id);
            await MarkProviderUnknownAsync(operationId, leg.Id, attempt.Id, actorId, cancellationToken);
        }
    }

    private async Task<ProviderRefundSnapshot> ReadVerifiedProviderRefundsAsync(
        Guid operationId,
        OrderAmendmentRefundLeg leg,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var providerContext = refundProvider.ReadContext();
        var journal = await ReadRefundJournalAsync(leg, cancellationToken);
        var canonical = await checkoutEvidence.ReadAsync(journal, false, cancellationToken);
        RequireCapturedCharge(leg, journal, canonical, providerContext);
        var providerRefunds = await refundProvider.ListForChargeAsync(leg.ProviderChargeId!, cancellationToken);
        var stored = await ReadChargeRefundEvidenceAsync(leg, cancellationToken);
        var scopes = await ReadRefundScopeMapsAsync(stored, leg, cancellationToken);
        var correlation = new RefundProviderCorrelation(
            scopes.Operations, scopes.Attempts, operationId, leg.Id, attemptId);
        var refunded = OrderAmendmentRefundProviderProof.RequireCanonicalHistory(
            stored, providerRefunds, providerContext, leg.ProviderChargeId!, leg.ProviderIntentId!,
            leg.Currency, correlation);
        if (canonical.Charge?.RefundedMinor != refunded)
            throw new ConflictException("The provider's captured refund total differs from immutable refund evidence.");
        return new ProviderRefundSnapshot(providerContext, providerRefunds);
    }

    private async Task<bool> HandleCurrentRefundAsync(
        Guid operationId,
        Guid legId,
        AmendmentRefundEvidence current,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await RecordProviderObservationAsync(operationId, legId, current, actorId, cancellationToken);
        if (current.Status == "succeeded")
        {
            await VerifyProviderLegAsync(operationId, legId, cancellationToken);
            return true;
        }
        if (current.Status is "pending" or "requires_action")
            return true;
        if (current.Status is not ("failed" or "canceled"))
            throw new ConflictException("The provider refund status requires reconciliation.");
        return false;
    }

    private async Task<(OrderAmendmentRefundLeg Leg, OrderAmendmentRefundAttempt Attempt)>
        PrepareProviderAttemptAsync(
        Guid operationId,
        OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundAttempt attempt,
        CancellationToken cancellationToken)
    {
        var latest = await ReadLatestLegObservationAsync(leg.Id, cancellationToken);
        if (latest is not null && latest.RefundAttemptId == attempt.Id
            && latest.ProviderRefundStatus is ("failed" or "canceled"))
        {
            leg = await AppendRetryAttemptAsync(operationId, leg.Id, attempt.Id, cancellationToken);
            attempt = leg.Attempts.OrderByDescending(value => value.Sequence).First();
        }
        else if (latest?.ProviderRefundStatus is "succeeded" or "pending" or "requires_action")
        {
            throw new ConflictException("The original provider refund request still needs reconciliation.");
        }
        else if (latest is not null && latest.FailureCode != ProviderOutcomeUnknownFailureCode
                 && latest.RefundAttemptId == attempt.Id)
        {
            throw new ConflictException("The original provider refund request still needs reconciliation.");
        }

        if (!resolutionPolicy.IsProviderRetrySafe(attempt.RequestedAt))
            throw new ConflictException("The provider idempotency window is too old for automatic retry.");
        return (leg, attempt);
    }

    private static AmendmentRefundRequest BuildProviderRequest(
        OrderAmendmentRefundLeg leg, Guid operationId, OrderAmendmentRefundAttempt attempt) => new(
        leg.ProviderChargeId!, leg.ProviderIntentId!, leg.Currency, leg.AmountMinor,
        attempt.IdempotencyKey, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StripeOrderAmendmentRefundProvider.SchemaKey] = StripeOrderAmendmentRefundProvider.SchemaVersion,
            [StripeOrderAmendmentRefundProvider.OperationKey] = operationId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.LegKey] = leg.Id.ToString("D"),
            [StripeOrderAmendmentRefundProvider.AttemptKey] = attempt.Id.ToString("D")
        });

    private sealed record ProviderRefundSnapshot(
        AmendmentRefundProviderContext Context,
        IReadOnlyList<AmendmentRefundEvidence> Refunds);

    private sealed record PreparedProviderRefund(
        OrderAmendmentRefundLeg Leg,
        OrderAmendmentRefundAttempt Attempt,
        AmendmentRefundProviderContext ProviderContext);

    private async Task<OrderAmendmentRefundLeg> LoadLegAsync(
        Guid operationId, Guid legId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentRefundLegs.AsNoTracking()
            .Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == legId && value.OperationId == operationId,
                cancellationToken)
        ?? throw Unavailable();

    private async Task<OrderAmendmentResolutionResultDto> ReadResultAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Include(value => value.Legs).ThenInclude(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken) ?? throw Unavailable();
        var legs = operation.Legs.ToArray();
        var legIds = legs.Select(value => value.Id).ToArray();
        var evidence = legIds.Length == 0 ? []
            : await context.OrderAmendmentRefundEvidence.AsNoTracking()
                .Where(value => legIds.Contains(value.RefundLegId)).ToListAsync(cancellationToken);
        return OrderAmendmentResolutionResultMapper.Map(operation, legs, evidence);
    }

    private Task<OrderAmendmentResolutionOperation?> FindOperationByClientKeyAsync(
        Guid actorId, Guid clientOperationId, CancellationToken cancellationToken) =>
        context.OrderAmendmentResolutionOperations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ActorUserId == actorId
                && value.ClientOperationId == clientOperationId, cancellationToken);
}
