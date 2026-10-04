using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCashRefundHistoryCapacityReader
{
    internal static async Task<AccountCashRefundHistoryCapacitySize> ReadQuoteSizeAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var attempts = context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId);
        var attemptCount = await CountWithinLimitAsync(attempts, cancellationToken);
        var allocationCount = await CountWithinLimitAsync(
            context.AccountPaymentAllocations.AsNoTracking()
                .Where(value => value.Attempt!.ServiceSessionId == serviceSessionId),
            cancellationToken);
        return AccountCashRefundHistoryCapacitySize.ForQuote(attemptCount, allocationCount);
    }

    internal static async Task<AccountCashRefundHistoryCapacitySize> ReadResolutionSizeAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var cashAttemptIds = context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId
                && value.PaymentMethod == PaymentMethod.Cash
                && value.CashCollectionReceipt != null)
            .Select(value => value.Id);
        var cashAllocationIds = context.AccountPaymentAllocations.AsNoTracking()
            .Where(value => cashAttemptIds.Contains(value.AttemptId))
            .Select(value => value.Id);
        var cashLegs = context.OrderAmendmentRefundLegs.AsNoTracking()
            .Where(value => value.AccountPaymentAttemptId.HasValue
                && cashAttemptIds.Contains(value.AccountPaymentAttemptId.Value));
        var cashLegIds = cashLegs.Select(value => value.Id);
        var cashOperationIds = cashLegs.Select(value => value.OperationId).Distinct();

        var attempts = await CountWithinLimitAsync(context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId), cancellationToken);
        var allocations = await CountWithinLimitAsync(
            context.AccountPaymentAllocations.AsNoTracking()
                .Where(value => value.Attempt!.ServiceSessionId == serviceSessionId),
            cancellationToken);
        var legs = await CountWithinLimitAsync(cashLegs, cancellationToken);
        var intents = await CountWithinLimitAsync(context.AccountCashRefundIntents.AsNoTracking()
            .Where(value => cashAttemptIds.Contains(value.AttemptId)), cancellationToken);
        var persistedReversals = await CountWithinLimitAsync(context.AccountPaymentAllocationReversals.AsNoTracking()
            .Where(value => cashAllocationIds.Contains(value.AllocationId)), cancellationToken);
        var pendingReversals = await CountPendingReversalReservationsAsync(
            context, serviceSessionId, cancellationToken);
        var reversalLimit = (long)AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1;
        var reversals = Math.Min(reversalLimit, persistedReversals + pendingReversals);
        var evidence = await CountWithinLimitAsync(context.OrderAmendmentRefundEvidence.AsNoTracking()
            .Where(value => cashLegIds.Contains(value.RefundLegId)), cancellationToken);
        var operations = await CountWithinLimitAsync(context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => cashOperationIds.Contains(value.Id)), cancellationToken);

        return new AccountCashRefundHistoryCapacitySize(
            attempts, allocations, legs, intents, reversals, evidence, operations);
    }

    private static Task<long> CountPendingReversalReservationsAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var overLimit = AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1;
        var cashMethod = PaymentMethod.Cash.ToString();
        var resolvedState = OrderAmendmentResolutionOperationState.Resolved.ToString();

        // The caller holds the service-session mutation lock, which serializes new reservations
        // with the finalizer that replaces these scopes with persisted allocation reversals.
        return context.Database.SqlQuery<long>($"""
            SELECT LEAST(COALESCE(SUM(reservation.scope_count), 0), {overLimit})::bigint AS "Value"
            FROM (
                SELECT CASE
                    WHEN jsonb_typeof(leg.frozen_scopes_json) = 'array'
                        THEN LEAST(jsonb_array_length(leg.frozen_scopes_json), {overLimit})
                    ELSE {overLimit}
                END::bigint AS scope_count
                FROM order_amendment_refund_legs AS leg
                JOIN order_amendment_resolution_operations AS operation
                    ON operation.id = leg.operation_id
                JOIN account_payment_attempts AS attempt
                    ON attempt.id = leg.account_payment_attempt_id
                WHERE attempt.service_session_id = {serviceSessionId}
                    AND attempt.payment_method = {cashMethod}
                    AND operation.state <> {resolvedState}
                    AND EXISTS (
                        SELECT 1
                        FROM account_cash_collection_receipts AS receipt
                        WHERE receipt.attempt_id = attempt.id
                    )
                LIMIT {overLimit}
            ) AS reservation
            """).SingleAsync(cancellationToken);
    }

    private static Task<long> CountWithinLimitAsync<T>(
        IQueryable<T> query, CancellationToken cancellationToken) =>
        query.Take(AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1)
            .LongCountAsync(cancellationToken);
}
