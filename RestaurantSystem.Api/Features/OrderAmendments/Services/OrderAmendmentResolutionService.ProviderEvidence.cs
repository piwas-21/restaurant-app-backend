using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task<bool> VerifyProviderLegAsync(
        Guid operationId, Guid legId, CancellationToken cancellationToken)
    {
        var leg = await LoadLegAsync(operationId, legId, cancellationToken);
        var attemptId = leg.Attempts.OrderByDescending(value => value.Sequence).First().Id;
        var journal = await ReadRefundJournalAsync(leg, cancellationToken);
        var providerContext = refundProvider.ReadContext();
        var canonical = await checkoutEvidence.ReadAsync(journal, false, cancellationToken);
        RequireCapturedCharge(leg, journal, canonical, providerContext);
        var providerRefunds = await refundProvider.ListForChargeAsync(leg.ProviderChargeId!, cancellationToken);
        var stored = await ReadChargeRefundEvidenceAsync(leg, cancellationToken);
        var scopes = await ReadRefundScopeMapsAsync(stored, leg, cancellationToken);
        var lastAttemptId = leg.Attempts.OrderByDescending(value => value.Sequence).First().Id;
        var correlation = new RefundProviderCorrelation(
            scopes.Operations, scopes.Attempts, operationId, legId, lastAttemptId);
        var total = OrderAmendmentRefundProviderProof.RequireCanonicalHistory(stored, providerRefunds,
            providerContext, leg.ProviderChargeId!, leg.ProviderIntentId!, leg.Currency, correlation);
        if (canonical.Charge?.RefundedMinor != total)
        {
            await SetProviderLegStateAsync(operationId, legId, attemptId,
                OrderAmendmentRefundLegState.ReconciliationRequired, cancellationToken);
            return false;
        }

        var succeeded = providerRefunds.Where(value => value.Status == "succeeded"
                && MetadataMatches(value, operationId, legId))
            .ToArray();
        if (succeeded.Length != 1 || succeeded[0].AmountMinor != leg.AmountMinor)
        {
            await SetProviderLegStateAsync(operationId, legId, attemptId,
                OrderAmendmentRefundLegState.ReconciliationRequired, cancellationToken);
            return false;
        }
        await SetProviderLegStateAsync(operationId, legId, attemptId,
            OrderAmendmentRefundLegState.Succeeded, cancellationToken,
            recoverFromReconciliation: true);
        return true;
    }

    private async Task<AccountCheckoutJournal> ReadRefundJournalAsync(
        OrderAmendmentRefundLeg leg, CancellationToken cancellationToken)
    {
        if (leg.AccountPaymentAttemptId is not Guid attemptId)
            throw new ConflictException("A provider refund leg lacks its captured account attempt.");
        return await context.AccountCheckoutJournals.AsNoTracking()
            .SingleOrDefaultAsync(value => value.AttemptId == attemptId, cancellationToken)
            ?? throw new ConflictException("The original captured payment context is unavailable.");
    }

    private static void RequireCapturedCharge(
        OrderAmendmentRefundLeg leg, AccountCheckoutJournal journal,
        AccountCheckoutCanonicalEvidence evidence, AmendmentRefundProviderContext providerContext)
    {
        var intent = evidence.Intent;
        var charge = evidence.Charge;
        if (journal.ReconciliationRequired || journal.Currency != leg.Currency
            || journal.ProviderAccountId != leg.ProviderAccountId
            || journal.ProviderLiveMode != leg.ProviderLiveMode
            || journal.ProviderChargeId != leg.ProviderChargeId
            || journal.ProviderIntentId != leg.ProviderIntentId
            || providerContext.ConnectedAccountId != leg.ProviderAccountId
            || providerContext.LiveMode != leg.ProviderLiveMode
            || intent is null || charge is null || intent.Id != leg.ProviderIntentId
            || charge.Id != leg.ProviderChargeId || charge.IntentId != leg.ProviderIntentId
            || charge.Context.ConnectedAccountId != leg.ProviderAccountId
            || charge.Context.LiveMode != leg.ProviderLiveMode
            || charge.CapturedMinor != journal.AmountMinor || charge.AmountMinor != journal.AmountMinor
            || charge.Status != "succeeded" || !charge.Paid || !charge.Captured || charge.Disputed
            || !AccountStripeEvidence.HasCaptured(AccountCheckoutCanonicalEvidence.Expect(journal),
                evidence.Session, intent))
            throw new ConflictException("The original direct-charge capture cannot be proven for refund.");
    }

    private async Task<IReadOnlyList<OrderAmendmentRefundEvidence>> ReadChargeRefundEvidenceAsync(
        OrderAmendmentRefundLeg leg, CancellationToken cancellationToken) =>
        await context.OrderAmendmentRefundEvidence.AsNoTracking()
            .Where(value => value.ProviderChargeId == leg.ProviderChargeId
                && value.ProviderAccountId == leg.ProviderAccountId
                && value.ProviderLiveMode == leg.ProviderLiveMode)
            .ToListAsync(cancellationToken);

    private async Task<RefundScopeMaps> ReadRefundScopeMapsAsync(
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        OrderAmendmentRefundLeg current, CancellationToken cancellationToken)
    {
        var legIds = stored.Select(value => value.RefundLegId).Append(current.Id).Distinct().ToArray();
        var attempts = stored.Where(value => value.RefundAttemptId.HasValue)
            .Select(value => value.RefundAttemptId!.Value)
            .Concat(current.Attempts.Select(value => value.Id)).Distinct().ToArray();
        var legs = await context.OrderAmendmentRefundLegs.AsNoTracking()
            .Where(value => legIds.Contains(value.Id))
            .Select(value => new { value.Id, value.OperationId })
            .ToDictionaryAsync(value => value.Id, value => value.OperationId, cancellationToken);
        var attemptLegs = attempts.Length == 0 ? new Dictionary<Guid, Guid>()
            : await context.OrderAmendmentRefundAttempts.AsNoTracking()
                .Where(value => attempts.Contains(value.Id))
                .Select(value => new { value.Id, value.RefundLegId })
                .ToDictionaryAsync(value => value.Id, value => value.RefundLegId, cancellationToken);
        if (legs.Count != legIds.Length || attemptLegs.Count != attempts.Length)
            throw new ConflictException("A stored provider refund identity is unavailable.");
        return new RefundScopeMaps(legs, attemptLegs);
    }

    private static AmendmentRefundEvidence? FindCurrentRefund(
        IReadOnlyList<AmendmentRefundEvidence> provider, Guid operationId, Guid legId, Guid attemptId)
    {
        var matches = provider.Where(value => MetadataMatches(value, operationId, legId)
                && value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var attempt)
                && attempt == attemptId.ToString("D"))
            .ToArray();
        if (matches.Length > 1)
            throw new ConflictException("The provider returned duplicate refunds for one frozen request.");
        return matches.SingleOrDefault();
    }

    private static bool MetadataMatches(AmendmentRefundEvidence value, Guid operationId, Guid legId) =>
        value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.OperationKey, out var operation)
        && operation == operationId.ToString("D")
        && value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.LegKey, out var leg)
        && leg == legId.ToString("D");

    private static void ValidateCurrentRefund(
        AmendmentRefundEvidence response, Guid operationId, Guid legId, Guid attemptId, long amountMinor)
    {
        if (!MetadataMatches(response, operationId, legId)
            || !response.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var attempt)
            || attempt != attemptId.ToString("D")
            || response.AmountMinor != amountMinor)
            throw new ConflictException("The provider response does not match the persisted refund request.");
    }

    private sealed record RefundScopeMaps(
        IReadOnlyDictionary<Guid, Guid> Operations, IReadOnlyDictionary<Guid, Guid> Attempts);
}
