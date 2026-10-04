using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentRefundProviderProof
{
    private const string SucceededStatus = "succeeded";
    private const string PendingStatus = "pending";
    private const string RequiresActionStatus = "requires_action";
    private const string FailedStatus = "failed";
    private const string CanceledStatus = "canceled";
    private const string UnknownOutcomeFailureCode = "provider_outcome_unknown";

    internal static long RequireCanonicalHistory(
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        IReadOnlyList<AmendmentRefundEvidence> provider,
        AmendmentRefundProviderContext context, string chargeId, string intentId, string currency,
        RefundProviderCorrelation correlation)
    {
        var latest = LatestStoredObservations(stored, context, chargeId, intentId, currency);
        var byId = latest.ToDictionary(value => value.ProviderRefundId!, StringComparer.Ordinal);
        var inventory = new ProviderRefundInventory(byId, new HashSet<string>(StringComparer.Ordinal));
        long refunded = 0;
        foreach (var value in provider)
        {
            refunded = checked(refunded + ValidateProviderRefund(
                value, context, chargeId, intentId, currency, correlation, inventory));
        }
        if (inventory.SavedById.Keys.Any(value => !inventory.Seen.Contains(value)))
            throw NeedsReconciliation();
        return refunded;
    }

    private static long ValidateProviderRefund(
        AmendmentRefundEvidence value,
        AmendmentRefundProviderContext context,
        string chargeId,
        string intentId,
        string currency,
        RefundProviderCorrelation correlation,
        ProviderRefundInventory inventory)
    {
        ValidateIdentity(value, context, chargeId, intentId, currency);
        if (!inventory.Seen.Add(value.RefundId))
            throw NeedsReconciliation();

        var (operationId, legId, attemptId) = ReadProviderIdentity(value);
        if (!IsStoredOrAdopted(operationId, legId, attemptId, correlation))
            throw NeedsReconciliation();

        if (inventory.SavedById.TryGetValue(value.RefundId, out var saved)
                ? saved.AmountMinor != value.AmountMinor
                    || saved.RefundLegId != legId || saved.RefundAttemptId != attemptId
                    || !string.Equals(saved.Currency, value.Currency, StringComparison.OrdinalIgnoreCase)
                    || !CanTransition(saved.ProviderRefundStatus, value.Status)
                : !IsCurrentUnrecorded(value, correlation))
        {
            throw NeedsReconciliation();
        }

        return value.Status == SucceededStatus ? value.AmountMinor : 0;
    }

    private static (Guid OperationId, Guid LegId, Guid AttemptId) ReadProviderIdentity(
        AmendmentRefundEvidence value) => (
        Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.OperationKey], "D"),
        Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.LegKey], "D"),
        Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.AttemptKey], "D"));

    private static bool IsStoredOrAdopted(
        Guid operationId, Guid legId, Guid attemptId, RefundProviderCorrelation correlation)
    {
        var matchesStored = correlation.OperationByLeg.TryGetValue(legId, out var storedOperationId)
            && storedOperationId == operationId
            && correlation.LegByAttempt.TryGetValue(attemptId, out var storedLegId)
            && storedLegId == legId;
        var matchesAdoption = operationId == correlation.AdoptOperationId
            && legId == correlation.AdoptLegId
            && attemptId == correlation.AdoptAttemptId;
        return matchesStored || matchesAdoption;
    }

    private static bool IsCurrentUnrecorded(
        AmendmentRefundEvidence value, RefundProviderCorrelation correlation)
    {
        var metadata = value.Metadata;
        return correlation.AdoptOperationId is Guid expectedOperation
            && correlation.AdoptLegId is Guid expectedLeg
            && correlation.AdoptAttemptId is Guid expectedAttempt
            && metadata.TryGetValue(StripeOrderAmendmentRefundProvider.OperationKey, out var operation)
            && string.Equals(operation, expectedOperation.ToString("D"), StringComparison.Ordinal)
            && metadata.TryGetValue(StripeOrderAmendmentRefundProvider.LegKey, out var leg)
            && string.Equals(leg, expectedLeg.ToString("D"), StringComparison.Ordinal)
            && metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var attempt)
            && string.Equals(attempt, expectedAttempt.ToString("D"), StringComparison.Ordinal);
    }

    private sealed record ProviderRefundInventory(
        IReadOnlyDictionary<string, OrderAmendmentRefundEvidence> SavedById,
        ISet<string> Seen);


    internal static long SumStoredSuccess(
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        AmendmentRefundProviderContext context, string chargeId, string intentId, string currency)
    {
        var latest = LatestStoredObservations(stored, context, chargeId, intentId, currency);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var value in latest)
        {
            if (!ids.Add(value.ProviderRefundId!))
                throw NeedsReconciliation();
            if (value.ProviderRefundStatus == SucceededStatus)
                total = checked(total + value.AmountMinor);
        }
        return total;
    }

    internal static void ValidateIdentity(AmendmentRefundEvidence value,
        AmendmentRefundProviderContext context, string chargeId, string intentId, string currency)
    {
        var metadata = value.Metadata;
        if (string.IsNullOrWhiteSpace(value.RefundId) || value.AmountMinor <= 0
            || value.ChargeId != chargeId || value.IntentId != intentId
            || value.Context != context || !string.Equals(value.Currency, currency, StringComparison.OrdinalIgnoreCase)
            || value.Status is not (SucceededStatus or PendingStatus or RequiresActionStatus
                or FailedStatus or CanceledStatus)
            || metadata.Count != 4
            || !metadata.TryGetValue(StripeOrderAmendmentRefundProvider.SchemaKey, out var schema)
            || schema != StripeOrderAmendmentRefundProvider.SchemaVersion
            || !metadata.TryGetValue(StripeOrderAmendmentRefundProvider.OperationKey, out var operation)
            || !Guid.TryParseExact(operation, "D", out _)
            || !metadata.TryGetValue(StripeOrderAmendmentRefundProvider.LegKey, out var leg)
            || !Guid.TryParseExact(leg, "D", out _)
            || !metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var attempt)
            || !Guid.TryParseExact(attempt, "D", out _))
            throw NeedsReconciliation();
    }

    private static OrderAmendmentRefundEvidence[] LatestStoredObservations(
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        AmendmentRefundProviderContext context, string chargeId, string intentId, string currency)
    {
        var providerRows = stored.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
            && value.ProviderRefundId is not null).ToArray();
        if (providerRows.Any(value => value.ProviderChargeId != chargeId
                || value.ProviderIntentId != intentId || value.ProviderAccountId != context.ConnectedAccountId
                || value.ProviderLiveMode != context.LiveMode
                || !string.Equals(value.Currency, currency, StringComparison.OrdinalIgnoreCase)
                || value.AmountMinor <= 0))
            throw NeedsReconciliation();
        var groups = providerRows.GroupBy(value => value.ProviderRefundId!, StringComparer.Ordinal).ToArray();
        if (groups.Any(group => group.Select(value => value.RefundLegId).Distinct().Count() != 1
                || group.Select(value => value.RefundAttemptId).Distinct().Count() != 1
                || group.Select(value => value.AmountMinor).Distinct().Count() != 1
                || group.Select(value => value.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1))
            throw NeedsReconciliation();
        foreach (var group in groups)
        {
            var history = group.OrderBy(value => value.Sequence).ToArray();
            if (history.Any(value => !StoredStateMatchesProviderStatus(value))
                || history.Zip(history.Skip(1), (previous, current) => CanTransition(
                    previous.ProviderRefundStatus, current.ProviderRefundStatus)).Any(value => !value))
                throw NeedsReconciliation();
        }
        return groups.Select(group => group.OrderBy(value => value.Sequence).Last()).ToArray();
    }

    private static ConflictException NeedsReconciliation() =>
        new("The provider refund history is not fully bound to this amendment. Reconciliation is required.");
}
