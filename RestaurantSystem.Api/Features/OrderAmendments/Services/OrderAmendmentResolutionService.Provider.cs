using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
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
                await ProcessStripeLegSafelyAsync(operationId, leg.Id,
                    leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault()?.Id ?? Guid.Empty,
                    actorId, cancellationToken);
            await finalizer.TryFinalizeAsync(operationId, actorId, cancellationToken);
        }
        return await ReadResultAsync(operationId, cancellationToken);
    }

    private async Task ProcessStripeLegSafelyAsync(
        Guid operationId, Guid legId, Guid observedAttemptId,
        Guid actorId, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessStripeLegAsync(operationId, legId, actorId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AccountCheckoutDiagnostics.Warn(logger, exception,
                AccountCheckoutFailurePhase.ProviderRecovery, legId);
            await MarkProviderUnknownAsync(operationId, legId, observedAttemptId, actorId, cancellationToken);
        }
    }

    private async Task ProcessStripeLegAsync(
        Guid operationId, Guid legId, Guid actorId, CancellationToken cancellationToken)
    {
        var leg = await LoadLegAsync(operationId, legId, cancellationToken);
        if (leg.Custody != OrderAmendmentRefundCustody.StripeDirect
            || leg.State == OrderAmendmentRefundLegState.Succeeded && await VerifyProviderLegAsync(
                operationId, legId, actorId, cancellationToken))
            return;

        var providerContext = refundProvider.ReadContext();
        var journal = await ReadRefundJournalAsync(leg, cancellationToken);
        var canonical = await checkoutEvidence.ReadAsync(journal, false, cancellationToken);
        RequireCapturedCharge(leg, journal, canonical, providerContext);
        var providerRefunds = await refundProvider.ListForChargeAsync(leg.ProviderChargeId!, cancellationToken);
        var stored = await ReadChargeRefundEvidenceAsync(leg, cancellationToken);
        var scopes = await ReadRefundScopeMapsAsync(stored, leg, cancellationToken);
        var attempt = leg.Attempts.OrderByDescending(value => value.Sequence).FirstOrDefault()
            ?? throw new ConflictException("The provider refund request identity is unavailable.");
        var refunded = OrderAmendmentRefundProviderProof.RequireCanonicalHistory(
            stored, providerRefunds, providerContext, leg.ProviderChargeId!, leg.ProviderIntentId!,
            leg.Currency, scopes.Operations, scopes.Attempts, operationId, legId, attempt.Id);
        if (canonical.Charge?.RefundedMinor != refunded)
            throw new ConflictException("The provider's captured refund total differs from immutable refund evidence.");

        var current = FindCurrentRefund(providerRefunds, operationId, legId, attempt.Id);
        if (current is not null)
        {
            await RecordProviderObservationAsync(operationId, legId, current, actorId, cancellationToken);
            if (current.Status == "succeeded")
            {
                await VerifyProviderLegAsync(operationId, legId, actorId, cancellationToken);
                return;
            }
            if (current.Status is "pending" or "requires_action")
                return;
            if (current.Status is not ("failed" or "canceled"))
                throw new ConflictException("The provider refund status requires reconciliation.");
        }

        var latest = await ReadLatestLegObservationAsync(legId, cancellationToken);
        if (latest is not null && latest.RefundAttemptId == attempt.Id
            && latest.ProviderRefundStatus is ("failed" or "canceled"))
        {
            leg = await AppendRetryAttemptAsync(operationId, legId, attempt.Id, cancellationToken);
            attempt = leg.Attempts.OrderByDescending(value => value.Sequence).First();
        }
        else if (latest?.ProviderRefundStatus is "succeeded" or "pending" or "requires_action")
        {
            throw new ConflictException("The original provider refund request still needs reconciliation.");
        }
        else if (latest is not null && latest.FailureCode != "provider_outcome_unknown"
                 && latest.RefundAttemptId == attempt.Id)
            throw new ConflictException("The original provider refund request still needs reconciliation.");

        if (clock.GetUtcNow().UtcDateTime - attempt.RequestedAt >= TimeSpan.FromHours(23))
            throw new ConflictException("The provider idempotency window is too old for automatic retry.");
        var request = BuildProviderRequest(leg, operationId, attempt);
        var response = await refundProvider.CreateAsync(request, cancellationToken);
        OrderAmendmentRefundProviderProof.ValidateIdentity(response, providerContext,
            leg.ProviderChargeId!, leg.ProviderIntentId!, leg.Currency);
        ValidateCurrentRefund(response, operationId, legId, attempt.Id, leg.AmountMinor);
        await RecordProviderObservationAsync(operationId, legId, response, actorId, cancellationToken);
        if (response.Status == "succeeded")
            await VerifyProviderLegAsync(operationId, legId, actorId, cancellationToken);
    }

    private AmendmentRefundRequest BuildProviderRequest(
        OrderAmendmentRefundLeg leg, Guid operationId, OrderAmendmentRefundAttempt attempt) => new(
        leg.ProviderChargeId!, leg.ProviderIntentId!, leg.Currency, leg.AmountMinor,
        attempt.IdempotencyKey, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StripeOrderAmendmentRefundProvider.SchemaKey] = StripeOrderAmendmentRefundProvider.SchemaVersion,
            [StripeOrderAmendmentRefundProvider.OperationKey] = operationId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.LegKey] = leg.Id.ToString("D"),
            [StripeOrderAmendmentRefundProvider.AttemptKey] = attempt.Id.ToString("D")
        });

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
