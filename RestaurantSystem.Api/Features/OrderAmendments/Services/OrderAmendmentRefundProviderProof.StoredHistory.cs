using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentRefundProviderProof
{
    internal static void RequireStoredAttemptHistory(
        OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation,
        IReadOnlyCollection<OrderAmendmentRefundEvidence> evidence,
        bool requireSuccess)
    {
        var attempts = leg.Attempts.OrderBy(value => value.Sequence).ToArray();
        var rows = evidence.ToArray();
        var observations = rows.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
            .OrderBy(value => value.Sequence).ToArray();
        var refundsById = observations.Where(value => value.ProviderRefundId is not null)
            .GroupBy(value => value.ProviderRefundId!, StringComparer.Ordinal).ToArray();
        ValidateStoredAttempts(leg, attempts);
        ValidateStoredEvidenceKindsAndRequests(rows, attempts, leg, operation);
        ValidateStoredObservations(observations, attempts, leg, operation);
        ValidateRefundHistories(refundsById);

        var latestStatusByAttempt = ReadLatestStatusesByAttempt(
            attempts, observations, refundsById, requireSuccess);
        ValidateFailedEarlierAttempts(attempts, latestStatusByAttempt);
        ValidateLatestAttemptState(leg, attempts, latestStatusByAttempt, requireSuccess);
    }

    private static void ValidateStoredAttempts(
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundAttempt[] attempts)
    {
        if (leg.Custody != OrderAmendmentRefundCustody.StripeDirect || attempts.Length == 0
            || leg.ProviderChargeId is null || leg.ProviderIntentId is null
            || leg.ProviderAccountId is null || leg.ProviderLiveMode is null
            || attempts.Where((value, index) => value.Sequence != index + 1
                || value.IdempotencyKey != $"amendment-refund:{value.Id:N}"
                || value.RequestedAt == default).Any())
            throw NeedsReconciliation();
    }

    private static void ValidateStoredEvidenceKindsAndRequests(
        IReadOnlyList<OrderAmendmentRefundEvidence> rows,
        OrderAmendmentRefundAttempt[] attempts,
        OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation)
    {
        if (rows.Any(value => value.Kind is not (OrderAmendmentRefundEvidenceKind.ProviderRequest
                or OrderAmendmentRefundEvidenceKind.ProviderObservation)))
            throw NeedsReconciliation();

        var requests = rows.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderRequest).ToArray();
        if (requests.Length != attempts.Length || attempts.Any(attempt => requests.Count(value =>
                value.RefundAttemptId == attempt.Id && IsBoundRequest(value, leg, operation)) != 1))
            throw NeedsReconciliation();
    }

    private static void ValidateStoredObservations(
        IReadOnlyList<OrderAmendmentRefundEvidence> observations,
        IReadOnlyList<OrderAmendmentRefundAttempt> attempts,
        OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation)
    {
        var attemptIds = attempts.Select(value => value.Id).ToHashSet();
        if (observations.Any(value => !value.RefundAttemptId.HasValue
                || !attemptIds.Contains(value.RefundAttemptId.Value) || !IsBoundObservation(value, leg, operation)))
            throw NeedsReconciliation();
    }

    private static void ValidateRefundHistories(
        IReadOnlyList<IGrouping<string, OrderAmendmentRefundEvidence>> refundsById)
    {
        if (refundsById.Any(HasInconsistentRefundIdentityOrStatus)
            || refundsById.GroupBy(value => value.First().RefundAttemptId).Any(group => group.Count() > 1))
            throw NeedsReconciliation();
    }

    private static bool HasInconsistentRefundIdentityOrStatus(
        IGrouping<string, OrderAmendmentRefundEvidence> group)
    {
        if (group.Select(value => value.RefundAttemptId).Distinct().Count() != 1
            || group.Select(value => value.AmountMinor).Distinct().Count() != 1
            || group.Select(value => value.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return true;

        var history = group.OrderBy(value => value.Sequence).ToArray();
        return history.Any(value => !StoredStateMatchesProviderStatus(value))
            || history.Zip(history.Skip(1), (previous, current) =>
                CanTransition(previous.ProviderRefundStatus, current.ProviderRefundStatus))
                .Any(value => !value);
    }

    private static Dictionary<Guid, string> ReadLatestStatusesByAttempt(
        IReadOnlyList<OrderAmendmentRefundAttempt> attempts,
        IReadOnlyList<OrderAmendmentRefundEvidence> observations,
        IReadOnlyList<IGrouping<string, OrderAmendmentRefundEvidence>> refundsById,
        bool requireSuccess) => attempts
        .Select(attempt => new
        {
            attempt.Id,
            Status = LatestStatusForAttempt(attempt, attempts[^1].Id, observations, refundsById, requireSuccess)
        })
        .Where(value => value.Status is not null)
        .ToDictionary(value => value.Id, value => value.Status!);

    private static string? LatestStatusForAttempt(
        OrderAmendmentRefundAttempt attempt,
        Guid latestAttemptId,
        IReadOnlyList<OrderAmendmentRefundEvidence> observations,
        IReadOnlyList<IGrouping<string, OrderAmendmentRefundEvidence>> refundsById,
        bool requireSuccess)
    {
        var attemptRows = observations.Where(value => value.RefundAttemptId == attempt.Id).ToArray();
        var refundGroups = refundsById.Where(group => group.Any(value => value.RefundAttemptId == attempt.Id)).ToArray();
        if (refundGroups.Length > 1)
            throw NeedsReconciliation();

        var latest = attemptRows.LastOrDefault();
        if (refundGroups.Length == 0)
        {
            if (attemptRows.Any(value => value.ProviderRefundId is not null
                    || value.FailureCode != UnknownOutcomeFailureCode)
                || requireSuccess || attempt.Id != latestAttemptId
                || latest is not null && latest.State != OrderAmendmentRefundLegState.ReconciliationRequired)
                throw NeedsReconciliation();
            return null;
        }

        var refundRows = refundGroups[0].OrderBy(value => value.Sequence).ToArray();
        var final = refundRows[^1];
        if (latest is null || latest.ProviderRefundId != final.ProviderRefundId
            || latest.ProviderRefundStatus != final.ProviderRefundStatus)
            throw NeedsReconciliation();
        return final.ProviderRefundStatus;
    }

    private static void ValidateFailedEarlierAttempts(
        OrderAmendmentRefundAttempt[] attempts,
        Dictionary<Guid, string> latestStatusByAttempt)
    {
        foreach (var attemptId in attempts.Take(attempts.Length - 1).Select(value => value.Id))
        {
            if (!latestStatusByAttempt.TryGetValue(attemptId, out var status)
                || status is not (FailedStatus or CanceledStatus))
                throw NeedsReconciliation();
        }
    }

    private static void ValidateLatestAttemptState(
        OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundAttempt[] attempts,
        Dictionary<Guid, string> latestStatusByAttempt,
        bool requireSuccess)
    {
        var latestAttemptId = attempts[^1].Id;
        var hasCurrentStatus = latestStatusByAttempt.TryGetValue(latestAttemptId, out var currentStatus);
        if (requireSuccess)
        {
            if (leg.State != OrderAmendmentRefundLegState.Succeeded
                || !hasCurrentStatus || currentStatus != SucceededStatus)
                throw NeedsReconciliation();
            return;
        }

        if (!hasCurrentStatus)
        {
            if (leg.State is not (OrderAmendmentRefundLegState.Processing
                    or OrderAmendmentRefundLegState.ReconciliationRequired))
                throw NeedsReconciliation();
            return;
        }

        var expected = currentStatus switch
        {
            SucceededStatus => OrderAmendmentRefundLegState.Succeeded,
            PendingStatus or RequiresActionStatus => OrderAmendmentRefundLegState.Pending,
            FailedStatus or CanceledStatus => OrderAmendmentRefundLegState.Failed,
            _ => (OrderAmendmentRefundLegState?)null
        };
        if (expected != leg.State)
            throw NeedsReconciliation();
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
            return value.ProviderRefundStatus is null && value.FailureCode == UnknownOutcomeFailureCode
                && value.State == OrderAmendmentRefundLegState.ReconciliationRequired;
        var expectedState = value.ProviderRefundStatus switch
        {
            SucceededStatus => OrderAmendmentRefundLegState.Succeeded,
            PendingStatus or RequiresActionStatus => OrderAmendmentRefundLegState.Pending,
            FailedStatus or CanceledStatus => OrderAmendmentRefundLegState.Failed,
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
        previous == current || (previous is PendingStatus or RequiresActionStatus)
        && (current is PendingStatus or RequiresActionStatus or SucceededStatus or FailedStatus or CanceledStatus);

    private static bool StoredStateMatchesProviderStatus(OrderAmendmentRefundEvidence value) =>
        value.ProviderRefundStatus switch
        {
            SucceededStatus => value.State == OrderAmendmentRefundLegState.Succeeded,
            PendingStatus or RequiresActionStatus => value.State == OrderAmendmentRefundLegState.Pending,
            FailedStatus or CanceledStatus => value.State == OrderAmendmentRefundLegState.Failed,
            _ => false
        };
}
