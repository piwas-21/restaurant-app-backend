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
    private sealed record TillConfirmationContext(
        OrderAmendmentResolutionOperation Operation, OrderAmendmentRefundLeg Leg,
        Guid ActorId, string Reference, ManualTillConfirmationRequest Request,
        OrderAccountMutationScope Scope);

    public async Task<OrderAmendmentResolutionResultDto> ConfirmTillAsync(
        Guid operationId, ManualTillConfirmationRequest request, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        ArgumentNullException.ThrowIfNull(request);
        var tillRequest = ValidateTillRequest(operationId, request);
        var orderId = await ReadTillSourceOrderIdAsync(operationId, cancellationToken);
        await EnsureLoyaltySettlementReadyAsync(operationId, cancellationToken);
        await ConfirmTillWithinScopeAsync(operationId, orderId, actorId, tillRequest.PaymentId,
            tillRequest.Reference, request, cancellationToken);

        await finalizer.TryFinalizeAsync(operationId, actorId, cancellationToken);
        return await ReadResultAsync(operationId, cancellationToken);
    }

    private static (Guid PaymentId, string Reference) ValidateTillRequest(
        Guid operationId, ManualTillConfirmationRequest request)
    {
        var reference = request.TillReference?.Trim();
        if (operationId == Guid.Empty || request.PaymentId == Guid.Empty || reference is null
            || !OrderAmendmentTillReferencePolicy.IsValid(reference)
            || request.CashReturnedMinor is < 0)
            throw new BadRequestException("Provide one payment and a safe till refund reference.");
        return (request.PaymentId, reference);
    }

    private async Task<Guid> ReadTillSourceOrderIdAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId)
            .Select(value => (Guid?)value.SourceOrderId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();

    private async Task EnsureLoyaltySettlementReadyAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        if (!await OrderAmendmentLoyaltyReservationManager.TryActivateHeldAsync(
                context, operationId, cancellationToken))
            throw new ConflictException("The exact loyalty clawback is held until the full point obligation is available.");
        if (!await OrderAmendmentLoyaltyReservationEvidence.AreRequiredOwnersAvailableAsync(
                context, operationId, cancellationToken))
            throw new ConflictException("The accepted loyalty owner is no longer available for settlement.");
    }

    private async Task ConfirmTillWithinScopeAsync(
        Guid operationId, Guid orderId, Guid actorId, Guid paymentId, string reference,
        ManualTillConfirmationRequest request, CancellationToken cancellationToken)
    {
        await using (var scope = await OrderAccountMutationScope.BeginAsync(
                         context, orderId, cancellationToken))
        {
            var operation = await ReadTillOperationAsync(operationId, cancellationToken);
            EnsureTillOperationActor(operation, actorId, orderId);
            await EnsureTillReservationAsync(operationId, cancellationToken);
            var leg = RequireTillLeg(operation, paymentId);
            await ValidateCashTillReturnAsync(operation, leg, leg.CashRefundIntent, request, cancellationToken);
            var evidence = await ReadTillEvidenceAsync(leg.Id, cancellationToken);
            var confirmation = new TillConfirmationContext(
                operation, leg, actorId, reference, request, scope);
            if (evidence.Length == 1)
                await ConfirmExistingTillEvidenceAsync(confirmation, evidence[0], cancellationToken);
            else
                await RecordTillEvidenceAsync(confirmation, evidence.Length, cancellationToken);
        }
    }

    private async Task<OrderAmendmentResolutionOperation> ReadTillOperationAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentResolutionOperations
            .Include(value => value.Legs).ThenInclude(value => value.Attempts)
            .Include(value => value.Legs).ThenInclude(value => value.CashRefundIntent!.ReturnEvidence)
            .Include(value => value.Legs).ThenInclude(value => value.CashRefundIntent!.CollectionReceipt)
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken)
        ?? throw Unavailable();

    private static void EnsureTillOperationActor(
        OrderAmendmentResolutionOperation operation, Guid actorId, Guid orderId)
    {
        if (operation.ActorUserId != actorId || operation.SourceOrderId != orderId
            || operation.ActorRole != UserRole.Admin.ToString())
            throw Unavailable();
    }

    private async Task EnsureTillReservationAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        if (!await OrderAmendmentLoyaltyReservationManager.IsReservedUnderOrderLockAsync(
                context, operationId, cancellationToken))
            throw new ConflictException("The exact loyalty clawback reservation is no longer available.");
    }

    private static OrderAmendmentRefundLeg RequireTillLeg(
        OrderAmendmentResolutionOperation operation, Guid paymentId)
    {
        var leg = operation.Legs.SingleOrDefault(value => value.SourcePaymentId == paymentId)
            ?? throw Unavailable();
        if (leg.Custody != OrderAmendmentRefundCustody.ManualTill)
            throw new ConflictException("This payment does not have a local till refund step.");
        if (!AccountAmendmentRefundIntegrity.HasNoProviderContext(leg))
            throw new ConflictException("The till refund step contains conflicting provider context.");
        return leg;
    }

    private async Task<OrderAmendmentRefundEvidence[]> ReadTillEvidenceAsync(
        Guid legId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentRefundEvidence.AsNoTracking()
            .Where(value => value.RefundLegId == legId)
            .ToArrayAsync(cancellationToken);

    private async Task ConfirmExistingTillEvidenceAsync(
        TillConfirmationContext confirmation, OrderAmendmentRefundEvidence evidence,
        CancellationToken cancellationToken)
    {
        EnsureSameTillConfirmation(confirmation.Operation, confirmation.Leg, evidence,
            confirmation.ActorId, confirmation.Reference, confirmation.Leg.CashRefundIntent,
            confirmation.Request.CashReturnedMinor);
        await confirmation.Scope.CommitAsync(cancellationToken);
    }

    private async Task RecordTillEvidenceAsync(
        TillConfirmationContext confirmation, int evidenceCount, CancellationToken cancellationToken)
    {
        var operation = confirmation.Operation;
        var leg = confirmation.Leg;
        if (evidenceCount != 0 || operation.State == OrderAmendmentResolutionOperationState.Resolved
            || leg.State != OrderAmendmentRefundLegState.Pending
            || leg.ManualTillReference is not null || leg.ResolvedAt is not null || leg.Attempts.Count != 0)
            throw new ConflictException("The till refund step already has different or incomplete evidence.");

        var now = resolutionPolicy.UtcNow;
        leg.ManualTillReference = confirmation.Reference;
        leg.State = OrderAmendmentRefundLegState.Succeeded;
        leg.ResolvedAt = now;
        leg.FailureCode = null;
        context.OrderAmendmentRefundEvidence.Add(NewEvidence(leg, null,
            await NextEvidenceSequenceAsync(leg.Id, cancellationToken),
            operation, new OrderAmendmentRefundObservation(OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
                OrderAmendmentRefundLegState.Succeeded, confirmation.ActorId, now,
                confirmation.Reference, null, currentUser.GetAuditIdentifier())));
        RecordCashReturnIfRequired(leg.CashRefundIntent, confirmation.ActorId,
            confirmation.Request, confirmation.Reference, now);
        await RefreshOperationStateAsync(operation, cancellationToken);
        operation.UpdatedAt = now;
        operation.UpdatedBy = currentUser.GetAuditIdentifier();
        await context.SaveChangesAsync(cancellationToken);
        await confirmation.Scope.CommitAsync(cancellationToken);
    }

    private void RecordCashReturnIfRequired(
        AccountCashRefundIntent? cashIntent, Guid actorId,
        ManualTillConfirmationRequest request, string reference, DateTime now)
    {
        if (cashIntent is not null)
            RecordCashReturnEvidence(cashIntent, actorId, request.CashReturnedMinor!.Value, reference, now);
    }

    private async Task ValidateCashTillReturnAsync(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        AccountCashRefundIntent? cashIntent, ManualTillConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, AccountCashRefundHistory> historyByAttempt =
            new Dictionary<Guid, AccountCashRefundHistory>();
        if (leg.AccountPaymentAttemptId is Guid attemptId)
            historyByAttempt = await AccountCashRefundHistoryReader.ReadAsync(
                context, [attemptId], cancellationToken, operation.Id);
        if (cashIntent is null)
        {
            if (request.CashReturnedMinor is not null
                || leg.AccountPaymentAttemptId is Guid missingIntentAttempt
                && historyByAttempt.ContainsKey(missingIntentAttempt))
                throw new ConflictException("This refund has no frozen physical cash return amount.");
        }
        else
        {
            if (!historyByAttempt.TryGetValue(cashIntent.AttemptId, out var history)
                || request.CashReturnedMinor != cashIntent.CashRefundAmountMinor
                || cashIntent.CollectionReceipt is null)
                throw new ConflictException("Confirm the exact physical return shown in the reviewed cash refund quote.");
            AccountCashRefundIntentValidator.RequireMatchesHistory(cashIntent, leg,
                operation, cashIntent.CollectionReceipt, history);
        }
    }

    private void RecordCashReturnEvidence(
        AccountCashRefundIntent cashIntent, Guid actorId, long cashReturnedMinor,
        string reference, DateTime now)
    {
        var returnEvidence = new AccountCashRefundEvidence
        {
            Id = Guid.NewGuid(),
            IntentId = cashIntent.Id,
            ExactRefundAmountMinor = cashIntent.ExactRefundAmountMinor,
            RefundAdjustmentMinor = cashIntent.RefundAdjustmentMinor,
            CashReturnedMinor = cashReturnedMinor,
            Currency = cashIntent.Currency,
            ActorId = actorId,
            ActorRole = UserRole.Admin,
            TillReference = reference,
            ObservedAt = now,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        cashIntent.ReturnEvidence = returnEvidence;
        context.AccountCashRefundEvidence.Add(returnEvidence);
    }

    private static void EnsureSameTillConfirmation(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundEvidence evidence, Guid actorId, string reference,
        AccountCashRefundIntent? cashIntent, long? cashReturnedMinor)
    {
        if (evidence.Kind != OrderAmendmentRefundEvidenceKind.ManualTillConfirmation
            || evidence.State != OrderAmendmentRefundLegState.Succeeded
            || evidence.AmountMinor != leg.AmountMinor || evidence.Currency != leg.Currency
            || evidence.TillReference != reference || leg.ManualTillReference != reference
            || leg.State != OrderAmendmentRefundLegState.Succeeded || leg.ResolvedAt != evidence.ObservedAt
            || evidence.ActorUserId != actorId || evidence.ActorUserId != operation.ActorUserId
            || evidence.ActorRole != operation.ActorRole || leg.Attempts.Count != 0)
            throw new ConflictException("The till refund step is already bound to different evidence.");
        if (cashIntent is null)
        {
            if (cashReturnedMinor is not null)
                throw new ConflictException("This refund has no frozen physical cash return amount.");
            return;
        }

        if (cashReturnedMinor != cashIntent.CashRefundAmountMinor)
            throw new ConflictException("The cash return confirmation differs from its frozen physical amount.");
        AccountCashRefundIntentValidator.RequireReturnEvidence(cashIntent, leg, operation,
            AccountCashRefundPlan.FromIntent(cashIntent).SettlementQuote(), cashIntent.ReturnEvidence);
    }
}
