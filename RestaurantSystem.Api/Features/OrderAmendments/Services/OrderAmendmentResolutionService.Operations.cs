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
    private OrderAmendmentResolutionOperation PersistOperation(OrderAmendmentResolutionPlanningState state,
        OrderAmendmentResolutionStartRequest request, string requestHash, Guid actorId)
    {
        var now = resolutionPolicy.UtcNow;
        var reviewedQuote = OrderAmendmentResolutionQuoteFactory.Create(
            state.Source.Id, state.Amendment.Id, actorId, request.Quote, state.Plan, request.ExpiresAt);
        if (reviewedQuote.QuoteHash != request.QuoteHash)
            throw new ConflictException("The reviewed amendment quote changed before it was persisted.");
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = Guid.NewGuid(),
            AmendmentId = state.Amendment.Id,
            SourceOrderId = state.Source.Id,
            ServiceSessionId = state.Source.ServiceSessionId,
            ClientOperationId = request.Quote.ClientOperationId,
            ActorUserId = actorId,
            ActorRole = currentUser.Role?.ToString() ?? UserRole.Admin.ToString(),
            ExpectedOrderVersion = request.Quote.ExpectedOrderVersion,
            ExpectedAccountRevision = request.Quote.ExpectedAccountRevision,
            Currency = state.Plan.Currency,
            CreditMinor = state.Plan.CreditMinor,
            RefundMinor = state.Plan.RefundMinor,
            UnpaidWaivedMinor = state.Plan.UnpaidWaivedMinor,
            RequestHash = requestHash,
            SnapshotJson = OrderAmendmentJson.Serialize(new OrderAmendmentResolutionSnapshot(
                request.QuoteHash, request.ExpiresAt, state.Plan.Currency,
                state.Plan.CreditMinor, state.Plan.RefundMinor, state.Plan.UnpaidWaivedMinor,
                requestHash, state.SourceFinancialFingerprint,
                OrderAmendmentResolutionPlanFingerprint.Create(state.Plan), request, reviewedQuote,
                state.Plan.Loyalty?.SnapshotId.HasValue == true ? state.Plan.Loyalty : null,
                OrderAmendmentLoyaltyPlanFingerprint.Version)),
            State = OrderAmendmentResolutionOperationState.Processing,
            StartedAt = now,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        var legs = state.Plan.Legs.Select(plan => CreateLeg(operation, plan, now, actorId)).ToArray();
        operation.Legs = legs;
        context.OrderAmendmentResolutionOperations.Add(operation);
        return operation;
    }

    private OrderAmendmentRefundLeg CreateLeg(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLegPlan plan,
        DateTime now, Guid actorId)
    {
        var leg = new OrderAmendmentRefundLeg
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            SourcePaymentId = plan.Payment.Id,
            AccountPaymentAttemptId = plan.AccountPaymentAttemptId,
            Custody = plan.Custody,
            State = plan.Custody == OrderAmendmentRefundCustody.ManualTill
                ? OrderAmendmentRefundLegState.Pending : OrderAmendmentRefundLegState.Processing,
            AmountMinor = plan.AmountMinor,
            Currency = operation.Currency,
            FrozenScopesJson = OrderAmendmentJson.Serialize(plan.Scopes),
            ProviderAccountId = plan.ProviderAccountId,
            ProviderLiveMode = plan.ProviderLiveMode,
            ProviderChargeId = plan.ProviderChargeId,
            ProviderIntentId = plan.ProviderIntentId,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        if (plan.Custody == OrderAmendmentRefundCustody.ManualTill)
        {
            if (plan.CashRefund is not null)
            {
                var cashIntent = CreateCashRefundIntent(operation, leg, plan.CashRefund, now);
                leg.CashRefundIntent = cashIntent;
                context.AccountCashRefundIntents.Add(cashIntent);
            }
            return leg;
        }

        var attemptId = Guid.NewGuid();
        var attempt = new OrderAmendmentRefundAttempt
        {
            Id = attemptId,
            RefundLegId = leg.Id,
            Sequence = 1,
            IdempotencyKey = $"amendment-refund:{attemptId:N}",
            RequestedAt = now,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        leg.Attempts.Add(attempt);
        context.OrderAmendmentRefundEvidence.Add(NewEvidence(leg, attempt, 1,
            operation, new OrderAmendmentRefundObservation(OrderAmendmentRefundEvidenceKind.ProviderRequest,
                OrderAmendmentRefundLegState.Processing, actorId, now,
                null, null, currentUser.GetAuditIdentifier())));
        return leg;
    }

    private AccountCashRefundIntent CreateCashRefundIntent(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        AccountCashRefundPlan plan, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            RefundLegId = leg.Id,
            OperationId = operation.Id,
            AttemptId = plan.AttemptId,
            CollectionReceiptId = plan.ReceiptId,
            PolicyVersion = plan.PolicyVersion,
            Currency = plan.Currency,
            OriginalExactAmountMinor = plan.OriginalExactAmountMinor,
            OriginalAdjustmentMinor = plan.OriginalAdjustmentMinor,
            OriginalDueAmountMinor = plan.OriginalDueAmountMinor,
            PreviouslyRefundedExactMinor = plan.PreviouslyRefundedExactMinor,
            PreviouslyRefundedCashMinor = plan.PreviouslyRefundedCashMinor,
            ExactRefundAmountMinor = plan.ExactRefundAmountMinor,
            RefundAdjustmentMinor = plan.RefundAdjustmentMinor,
            CashRefundAmountMinor = plan.CashRefundAmountMinor,
            RetainedExactAmountMinor = plan.RetainedExactAmountMinor,
            RetainedCashDueMinor = plan.RetainedCashDueMinor,
            PriorHistoryFingerprint = plan.PriorHistoryFingerprint,
            CreatedAt = now,
            CreatedBy = currentUser.GetAuditIdentifier()
        };

    private static OrderAmendmentRefundEvidence NewEvidence(
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundAttempt? attempt, int sequence,
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundObservation observation)
    {
        var (kind, state, actorId, now, tillReference, provider, auditIdentifier) = observation;
        return new()
        {
            Id = Guid.NewGuid(),
            RefundLegId = leg.Id,
            RefundAttemptId = attempt?.Id,
            Sequence = sequence,
            Kind = kind,
            State = state,
            AmountMinor = provider?.AmountMinor ?? leg.AmountMinor,
            Currency = provider?.Currency ?? leg.Currency,
            ActorUserId = actorId,
            ActorRole = operation.ActorRole,
            ObservedAt = now,
            FailureCode = ProviderFailureCode(state),
            TillReference = tillReference,
            ProviderRefundId = provider?.RefundId,
            ProviderRefundStatus = provider?.Status,
            ProviderChargeId = provider?.ChargeId ?? leg.ProviderChargeId,
            ProviderIntentId = provider?.IntentId ?? leg.ProviderIntentId,
            ProviderAccountId = provider?.Context.ConnectedAccountId ?? leg.ProviderAccountId,
            ProviderLiveMode = provider?.Context.LiveMode ?? leg.ProviderLiveMode,
            EvidenceJson = "{}",
            CreatedBy = auditIdentifier
        };
    }

}
