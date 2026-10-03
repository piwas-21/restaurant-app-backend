using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentRefundProviderProof
{
    internal static long RequireCanonicalHistory(
        IReadOnlyList<OrderAmendmentRefundEvidence> stored,
        IReadOnlyList<AmendmentRefundEvidence> provider,
        AmendmentRefundProviderContext context, string chargeId, string intentId, string currency,
        IReadOnlyDictionary<Guid, Guid> operationByLeg,
        IReadOnlyDictionary<Guid, Guid> legByAttempt,
        Guid? adoptOperationId = null, Guid? adoptLegId = null, Guid? adoptAttemptId = null)
    {
        var latest = LatestStoredObservations(stored, context, chargeId, intentId, currency);
        var byId = latest.ToDictionary(value => value.ProviderRefundId!, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long refunded = 0;
        foreach (var value in provider)
        {
            ValidateIdentity(value, context, chargeId, intentId, currency);
            if (!seen.Add(value.RefundId))
                throw NeedsReconciliation();
            var operationId = Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.OperationKey], "D");
            var legId = Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.LegKey], "D");
            var attemptId = Guid.ParseExact(value.Metadata[StripeOrderAmendmentRefundProvider.AttemptKey], "D");
            if (!operationByLeg.TryGetValue(legId, out var storedOperationId)
                || storedOperationId != operationId || !legByAttempt.TryGetValue(attemptId, out var storedLegId)
                || storedLegId != legId)
            {
                if (operationId != adoptOperationId || legId != adoptLegId || attemptId != adoptAttemptId)
                    throw NeedsReconciliation();
            }
            if (byId.TryGetValue(value.RefundId, out var saved))
            {
                if (saved.AmountMinor != value.AmountMinor
                    || saved.RefundLegId != legId || saved.RefundAttemptId != attemptId
                    || !string.Equals(saved.Currency, value.Currency, StringComparison.OrdinalIgnoreCase)
                    || !CanTransition(saved.ProviderRefundStatus, value.Status))
                    throw NeedsReconciliation();
            }
            else if (!IsCurrentUnrecorded(value, adoptOperationId, adoptLegId, adoptAttemptId))
            {
                throw NeedsReconciliation();
            }
            if (value.Status == "succeeded")
                refunded = checked(refunded + value.AmountMinor);
        }
        if (byId.Keys.Any(value => !seen.Contains(value)))
            throw NeedsReconciliation();
        return refunded;
    }

    internal static void RequireStoredAttemptHistory(
        OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation,
        IReadOnlyCollection<OrderAmendmentRefundEvidence> evidence,
        bool requireSuccess)
    {
        var attempts = leg.Attempts.OrderBy(value => value.Sequence).ToArray();
        if (leg.Custody != OrderAmendmentRefundCustody.StripeDirect || attempts.Length == 0
            || attempts.Where((value, index) => value.Sequence != index + 1
                || value.IdempotencyKey != $"amendment-refund:{value.Id:N}"
                || value.RequestedAt == default).Any()
            || leg.ProviderChargeId is null || leg.ProviderIntentId is null
            || leg.ProviderAccountId is null || leg.ProviderLiveMode is null)
            throw NeedsReconciliation();

        var rows = evidence.ToArray();
        if (rows.Any(value => value.Kind is not (OrderAmendmentRefundEvidenceKind.ProviderRequest
                or OrderAmendmentRefundEvidenceKind.ProviderObservation)))
            throw NeedsReconciliation();
        var requests = rows.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderRequest).ToArray();
        if (requests.Length != attempts.Length || attempts.Any(attempt => requests.Count(value =>
                value.RefundAttemptId == attempt.Id && IsBoundRequest(value, leg, operation)) != 1))
            throw NeedsReconciliation();

        var observations = rows.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
            .OrderBy(value => value.Sequence).ToArray();
        var attemptIds = attempts.Select(value => value.Id).ToHashSet();
        if (observations.Any(value => !value.RefundAttemptId.HasValue
                || !attemptIds.Contains(value.RefundAttemptId.Value) || !IsBoundObservation(value, leg, operation)))
            throw NeedsReconciliation();

        var refundsById = observations.Where(value => value.ProviderRefundId is not null)
            .GroupBy(value => value.ProviderRefundId!, StringComparer.Ordinal).ToArray();
        if (refundsById.Any(group => group.Select(value => value.RefundAttemptId).Distinct().Count() != 1
                || group.Select(value => value.AmountMinor).Distinct().Count() != 1
                || group.Select(value => value.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1
                || group.OrderBy(value => value.Sequence).Zip(group.OrderBy(value => value.Sequence).Skip(1),
                    (previous, current) => CanTransition(previous.ProviderRefundStatus, current.ProviderRefundStatus))
                    .Any(value => !value))
            || refundsById.GroupBy(value => value.First().RefundAttemptId).Any(group => group.Count() > 1))
            throw NeedsReconciliation();

        var latestStatusByAttempt = new Dictionary<Guid, string>();
        foreach (var attempt in attempts)
        {
            var attemptRows = observations.Where(value => value.RefundAttemptId == attempt.Id).ToArray();
            var refundGroups = refundsById.Where(group => group.Any(value => value.RefundAttemptId == attempt.Id)).ToArray();
            if (refundGroups.Length > 1)
                throw NeedsReconciliation();
            var latest = attemptRows.LastOrDefault();
            if (refundGroups.Length == 0)
            {
                if (attemptRows.Any(value => value.ProviderRefundId is not null || value.FailureCode != "provider_outcome_unknown")
                    || requireSuccess || attempt.Id != attempts[^1].Id
                    || latest is not null && latest.State != OrderAmendmentRefundLegState.ReconciliationRequired)
                    throw NeedsReconciliation();
                continue;
            }

            var refundRows = refundGroups[0].OrderBy(value => value.Sequence).ToArray();
            var final = refundRows[^1];
            if (latest is null || latest.ProviderRefundId != final.ProviderRefundId
                || latest.ProviderRefundStatus != final.ProviderRefundStatus)
                throw NeedsReconciliation();
            latestStatusByAttempt.Add(attempt.Id, final.ProviderRefundStatus!);
        }

        for (var index = 0; index < attempts.Length - 1; index++)
        {
            if (!latestStatusByAttempt.TryGetValue(attempts[index].Id, out var status)
                || status is not ("failed" or "canceled"))
                throw NeedsReconciliation();
        }

        if (requireSuccess && (leg.State != OrderAmendmentRefundLegState.Succeeded
                || !latestStatusByAttempt.TryGetValue(attempts[^1].Id, out var latestStatus)
                || latestStatus != "succeeded"))
            throw NeedsReconciliation();

        var hasCurrentStatus = latestStatusByAttempt.TryGetValue(attempts[^1].Id, out var currentStatus);
        if (!requireSuccess && !hasCurrentStatus)
        {
            if (leg.State is not (OrderAmendmentRefundLegState.Processing
                    or OrderAmendmentRefundLegState.ReconciliationRequired))
                throw NeedsReconciliation();
        }
        else if (!requireSuccess)
        {
            var expected = currentStatus switch
            {
                "succeeded" => OrderAmendmentRefundLegState.Succeeded,
                "pending" or "requires_action" => OrderAmendmentRefundLegState.Pending,
                "failed" or "canceled" => OrderAmendmentRefundLegState.Failed,
                _ => (OrderAmendmentRefundLegState?)null
            };
            if (expected != leg.State)
                throw NeedsReconciliation();
        }
    }

    private static bool IsBoundRequest(OrderAmendmentRefundEvidence value,
        OrderAmendmentRefundLeg leg, OrderAmendmentResolutionOperation operation) =>
        value.Sequence > 0 && value.ObservedAt != default
        && value.State == OrderAmendmentRefundLegState.Processing
        && value.AmountMinor == leg.AmountMinor && value.Currency == leg.Currency
        && value.ActorUserId == operation.ActorUserId && value.ActorRole == operation.ActorRole
        && value.ProviderChargeId == leg.ProviderChargeId && value.ProviderIntentId == leg.ProviderIntentId
        && value.ProviderAccountId == leg.ProviderAccountId && value.ProviderLiveMode == leg.ProviderLiveMode
        && value.ProviderRefundId is null && value.ProviderRefundStatus is null
        && value.FailureCode is null && value.TillReference is null;

    private static bool IsBoundObservation(OrderAmendmentRefundEvidence value,
        OrderAmendmentRefundLeg leg, OrderAmendmentResolutionOperation operation)
    {
        if (value.Sequence <= 0 || value.ObservedAt == default
            || value.AmountMinor != leg.AmountMinor
            || !string.Equals(value.Currency, leg.Currency, StringComparison.OrdinalIgnoreCase)
            || value.ActorUserId != operation.ActorUserId || value.ActorRole != operation.ActorRole
            || value.ProviderChargeId != leg.ProviderChargeId || value.ProviderIntentId != leg.ProviderIntentId
            || value.ProviderAccountId != leg.ProviderAccountId || value.ProviderLiveMode != leg.ProviderLiveMode
            || value.TillReference is not null)
            return false;
        if (value.ProviderRefundId is null)
            return value.ProviderRefundStatus is null && value.FailureCode == "provider_outcome_unknown"
                && value.State == OrderAmendmentRefundLegState.ReconciliationRequired;
        var expectedState = value.ProviderRefundStatus switch
        {
            "succeeded" => OrderAmendmentRefundLegState.Succeeded,
            "pending" or "requires_action" => OrderAmendmentRefundLegState.Pending,
            "failed" or "canceled" => OrderAmendmentRefundLegState.Failed,
            _ => (OrderAmendmentRefundLegState?)null
        };
        var expectedFailure = expectedState switch
        {
            OrderAmendmentRefundLegState.Pending => "provider_refund_pending",
            OrderAmendmentRefundLegState.Failed => "provider_refund_failed",
            _ => null
        };
        return !string.IsNullOrWhiteSpace(value.ProviderRefundId) && expectedState == value.State
            && value.FailureCode == expectedFailure;
    }

    private static bool CanTransition(string? previous, string? current) =>
        previous == current || (previous is "pending" or "requires_action")
        && (current is "pending" or "requires_action" or "succeeded" or "failed" or "canceled");

    private static bool StoredStateMatchesProviderStatus(OrderAmendmentRefundEvidence value) =>
        value.ProviderRefundStatus switch
        {
            "succeeded" => value.State == OrderAmendmentRefundLegState.Succeeded,
            "pending" or "requires_action" => value.State == OrderAmendmentRefundLegState.Pending,
            "failed" or "canceled" => value.State == OrderAmendmentRefundLegState.Failed,
            _ => false
        };

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
            if (value.ProviderRefundStatus == "succeeded")
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
            || value.Status is not ("succeeded" or "pending" or "requires_action" or "failed" or "canceled")
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

    private static bool IsCurrentUnrecorded(AmendmentRefundEvidence value,
        Guid? operationId, Guid? legId, Guid? attemptId) => operationId is Guid expectedOperation
        && legId is Guid expectedLeg
        && attemptId is Guid expectedAttempt
        && value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.OperationKey, out var operation)
        && operation == expectedOperation.ToString("D")
        && value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.LegKey, out var leg)
        && leg == expectedLeg.ToString("D")
        && value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var attempt)
        && attempt == expectedAttempt.ToString("D");

    private static ConflictException NeedsReconciliation() =>
        new("The provider refund history is not fully bound to this amendment. Reconciliation is required.");
}
