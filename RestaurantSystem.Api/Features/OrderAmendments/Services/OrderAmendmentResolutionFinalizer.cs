using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Posts the frozen refund and credit only after every custody leg has durable proof.</summary>
public sealed partial class OrderAmendmentResolutionFinalizer(
    ApplicationDbContext context, ICurrentUserService currentUser, TimeProvider clock)
    : IOrderAmendmentResolutionFinalizer
{
    public async Task TryFinalizeAsync(
        Guid operationId, Guid actorId, CancellationToken cancellationToken)
    {
        ResetVerifiedCleanRequestContext();

        var sourceOrderId = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId).Select(value => (Guid?)value.SourceOrderId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The amendment resolution operation is unavailable.");

        await using var scope = await OrderAccountMutationScope.BeginAsync(
            context, sourceOrderId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations
            .Include(value => value.Legs).ThenInclude(value => value.Attempts)
            .Include(value => value.Legs).ThenInclude(value => value.CashRefundIntent!.ReturnEvidence)
            .Include(value => value.Legs).ThenInclude(value => value.CashRefundIntent!.CollectionReceipt)
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken)
            ?? throw new NotFoundException("The amendment resolution operation is unavailable.");
        if (operation.State is OrderAmendmentResolutionOperationState.Resolved
            or OrderAmendmentResolutionOperationState.ReconciliationRequired)
        {
            await scope.CommitAsync(cancellationToken);
            return;
        }
        var legs = operation.Legs.ToArray();
        if (legs.Any(value => value.State != OrderAmendmentRefundLegState.Succeeded))
            return;
        if (legs.Sum(value => value.AmountMinor) != operation.RefundMinor)
            throw new ConflictException("The refund legs do not match the frozen amendment amount.");

        var source = await context.Orders
            .Include(value => value.Payments)
            .Include(value => value.Items)
            .Include(value => value.ServiceSession)
            .AsSplitQuery()
            .SingleOrDefaultAsync(value => value.Id == operation.SourceOrderId && !value.IsDeleted,
            cancellationToken)
            ?? throw new NotFoundException("The amendment source order is unavailable.");
        if (source.Status is OrderStatus.Cancelled or OrderStatus.Refunded
            || source.PaymentStatus == PaymentStatus.Refunded)
            throw new ConflictException("A terminal or fully refunded source cannot be settled.");
        if (source.ServiceSession is not null
            && source.ServiceSession.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("The table account must remain open until amendment refunds are resolved.");
        await LoadAllocationEvidenceAsync(legs, cancellationToken);
        var amendment = await context.OrderAmendments.SingleOrDefaultAsync(value =>
                value.Id == operation.AmendmentId && value.SourceOrderId == source.Id,
            cancellationToken)
            ?? throw new ConflictException("The committed amendment is unavailable.");
        ValidateOperationSource(operation, amendment, source);
        var snapshot = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operation.SnapshotJson);
        if (snapshot.RequestHash != operation.RequestHash || snapshot.Currency != operation.Currency
            || snapshot.CreditMinor != operation.CreditMinor || snapshot.RefundMinor != operation.RefundMinor
            || snapshot.UnpaidWaivedMinor != operation.UnpaidWaivedMinor
            || string.IsNullOrWhiteSpace(snapshot.SourceFinancialFingerprint)
            || string.IsNullOrWhiteSpace(snapshot.PlanFingerprint))
            throw new ConflictException("The frozen amendment resolution snapshot is incomplete.");
        var money = new AccountMoney(operation.Currency);
        var sourceFingerprint = await OrderAmendmentFinancialSourceFingerprintReader.ReadAsync(
            context, source, amendment, operation.Id, money, cancellationToken);
        var planFingerprint = OrderAmendmentResolutionPlanFingerprint.Create(operation, legs);
        if (!string.Equals(snapshot.SourceFinancialFingerprint, sourceFingerprint, StringComparison.Ordinal)
            || !string.Equals(snapshot.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
            throw new ConflictException("The source financial evidence changed while the refund was being resolved.");
        var loyaltyEvidence = await OrderAmendmentLoyaltyEvidenceReader.ReadAsync(
            context, source.Id, cancellationToken);
        var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        var currentLoyaltyPlan = OrderAmendmentLoyaltyPlanner.Build(
            source, amendment, changes, money, loyaltyEvidence.WithoutOperation(operation.Id));
        OrderAmendmentLoyaltyResolutionVerifier.AssertPlanTransition(source.Id, amendment.Id,
            snapshot.LoyaltyPlan, currentLoyaltyPlan, loyaltyEvidence);
        var priorRefunds = await ReadPriorRefundsAsync(source, amendment.Id,
            operation.Id, money,
            cancellationToken: cancellationToken);
        ValidateCashRefundIntents(operation, legs, priorRefunds.CashRefundHistoryByAttempt);
        OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, operation, legs);

        var now = clock.GetUtcNow().UtcDateTime;
        var audit = currentUser.GetAuditIdentifier();
        ApplyRefunds(source, legs, priorRefunds.AuthorizedRefundMinorByPayment, money, now, audit);
        AddAllocationReversals(operation, actorId, audit, now);
        AddBillingCredit(source, amendment, operation, money, now, audit);
        await OrderAmendmentLoyaltyCompensationPoster.ApplyAsync(context, operation,
            snapshot.LoyaltyPlan, loyaltyEvidence, now, audit, cancellationToken);
        OrderAmendmentSourcePaymentSummary.Recalculate(source, money, now);

        source.Version++;
        source.UpdatedAt = now;
        source.UpdatedBy = audit;
        operation.State = OrderAmendmentResolutionOperationState.Resolved;
        operation.FailureCode = null;
        operation.ResolvedAt = now;
        operation.UpdatedAt = now;
        operation.UpdatedBy = audit;
        var loyaltyHeaders = context.OrderAmendmentLoyaltyCompensations.Local
            .Where(value => value.OperationId == operation.Id).ToArray();
        var loyaltyHeaderIds = loyaltyHeaders.Select(value => value.Id).ToHashSet();
        var loyaltyResult = OrderAmendmentLoyaltyResultFactory.Create(operation,
            new OrderAmendmentLoyaltyResultEvidence(snapshot.LoyaltyPlan,
                loyaltyEvidence.AwardWitness, loyaltyEvidence.OwnerLinks, loyaltyHeaders,
                context.OrderAmendmentLoyaltyReservations.Local
                    .Where(value => value.OperationId == operation.Id).ToArray(),
                context.OrderAmendmentLoyaltyCompensationPostings.Local
                    .Where(value => loyaltyHeaderIds.Contains(value.CompensationId)).ToArray(), null));
        ApplyFinancialResolution(amendment, operation, money, loyaltyResult);
        await OrderAmendmentLoyaltyReservationEvidence.ReleaseOwnerHoldsAsync(
            context, operation, now, cancellationToken);
        operation.ResultJson = OrderAmendmentJson.Serialize(CreateResult(operation, loyaltyResult));
        scope.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await OrderBillingCreditConsistency.AssertAsync(context, [source.Id], cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private async Task<AccountAmendmentRefundSnapshot> ReadPriorRefundsAsync(
        Order source, Guid currentAmendmentId, Guid currentOperationId,
        AccountMoney money, CancellationToken cancellationToken)
    {
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id && value.State == OrderAmendmentState.Committed
                && value.Id != currentAmendmentId)
            .ToListAsync(cancellationToken);
        if (amendments.Any(value => OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve an earlier amendment before posting another refund.");
        var attempts = source.ServiceSessionId is not Guid sessionId ? []
            : await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == sessionId
                    && value.Allocations.Any(allocation => allocation.OrderId == source.Id))
                .Include(value => value.Allocations.Where(allocation => allocation.OrderId == source.Id))
                .ToListAsync(cancellationToken);
        return await AccountAmendmentRefundIntegrity.ReadAsync(context, [source], amendments,
            attempts, money, cancellationToken, currentOperationId);
    }

    private static void ValidateOperationSource(
        OrderAmendmentResolutionOperation operation, OrderAmendment amendment, Order source)
    {
        if (operation.SourceOrderId != source.Id || operation.ActorRole != UserRole.Admin.ToString()
            || amendment.State != OrderAmendmentState.Committed
            || amendment.ServiceSessionId != source.ServiceSessionId
            || operation.ServiceSessionId != source.ServiceSessionId
            || amendment.ActorUserId == Guid.Empty || operation.ActorUserId == Guid.Empty)
            throw new ConflictException("The source account changed while the amendment refund was being resolved.");
    }

    private static void ApplyRefunds(Order source,
        IReadOnlyCollection<OrderAmendmentRefundLeg> legs,
        IReadOnlyDictionary<Guid, long> priorAuthorizedRefundMinorByPayment,
        AccountMoney money, DateTime now, string audit)
    {
        foreach (var group in legs.GroupBy(value => value.SourcePaymentId))
        {
            var payment = source.Payments.SingleOrDefault(value => value.Id == group.Key)
                ?? throw new ConflictException("A source payment disappeared during amendment resolution.");
            var refundMinor = group.Sum(value => value.AmountMinor);
            var capturedMinor = money.ToMinor(payment.Amount);
            var existingRefundMinor = money.ToMinor(payment.RefundedAmount
                ?? (payment.IsRefunded || payment.Status == PaymentStatus.Refunded ? payment.Amount : 0m));
            if (!payment.Status.IsCaptured()
                || existingRefundMinor != priorAuthorizedRefundMinorByPayment.GetValueOrDefault(payment.Id)
                || refundMinor <= 0
                || existingRefundMinor > capturedMinor
                || refundMinor > capturedMinor - existingRefundMinor)
                throw new ConflictException("The source tender changed while its amendment refund was pending.");

            var refunded = checked(existingRefundMinor + refundMinor);
            payment.RefundedAmount = money.ToMajor(refunded);
            payment.IsRefunded = refunded == capturedMinor;
            payment.RefundDate = now;
            payment.RefundReason = $"Order amendment {group.First().OperationId:N}";
            payment.Status = payment.IsRefunded
                ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = now;
            payment.UpdatedBy = audit;
        }
    }

    private void AddAllocationReversals(
        OrderAmendmentResolutionOperation operation, Guid actorId, string audit, DateTime now)
    {
        var scopes = operation.Legs.SelectMany(leg =>
            OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson)
                .Select(scope => (Leg: leg, Scope: scope))).ToArray();
        foreach (var group in scopes.GroupBy(value => value.Scope.AllocationId))
        {
            var allocation = context.AccountPaymentAllocations.Local
                .SingleOrDefault(value => value.Id == group.Key);
            if (allocation is null)
                throw new ConflictException("A frozen captured allocation is unavailable.");
            var existing = context.AccountPaymentAllocationReversals.Local
                .Where(value => value.AllocationId == allocation.Id).ToArray();
            var allRanges = existing.Select(value => (Start: value.StartOrdinal,
                    End: (long)value.StartOrdinal + value.UnitCount))
                .Concat(group.Select(value => (Start: value.Scope.StartOrdinal,
                    End: (long)value.Scope.StartOrdinal + value.Scope.UnitCount)))
                .OrderBy(value => value.Start).ToArray();
            for (var index = 1; index < allRanges.Length; index++)
            {
                if (allRanges[index].Start < allRanges[index - 1].End)
                    throw new ConflictException("A captured allocation unit already has refund evidence.");
            }

            foreach (var entry in group)
            {
                var frozen = entry.Scope;
                if (frozen.OrderId != allocation.OrderId || frozen.OrderItemId != allocation.OrderItemId
                    || frozen.MinorPerUnit != allocation.MinorPerUnit || frozen.UnitCount <= 0
                    || frozen.StartOrdinal < allocation.StartOrdinal
                    || (long)frozen.StartOrdinal + frozen.UnitCount
                        > (long)allocation.StartOrdinal + allocation.UnitCount
                    || frozen.AmountMinor != checked(frozen.MinorPerUnit * frozen.UnitCount)
                    || entry.Leg.AccountPaymentAttemptId != allocation.AttemptId
                    || entry.Leg.SourcePaymentId != allocation.OrderPaymentId)
                    throw new ConflictException("The refund scope no longer matches the captured tender allocation.");
                context.AccountPaymentAllocationReversals.Add(new AccountPaymentAllocationReversal
                {
                    Id = Guid.NewGuid(),
                    AllocationId = allocation.Id,
                    RefundLegId = entry.Leg.Id,
                    OrderId = frozen.OrderId,
                    OrderItemId = frozen.OrderItemId,
                    StartOrdinal = frozen.StartOrdinal,
                    UnitCount = frozen.UnitCount,
                    MinorPerUnit = frozen.MinorPerUnit,
                    AmountMinor = frozen.AmountMinor,
                    Currency = operation.Currency,
                    ActorUserId = actorId,
                    ActorRole = UserRole.Admin.ToString(),
                    ReversedAt = now,
                    CreatedBy = audit
                });
            }
        }
    }

    private void AddBillingCredit(Order source, OrderAmendment amendment,
        OrderAmendmentResolutionOperation operation, AccountMoney money, DateTime now, string audit)
    {
        var existing = context.OrderBillingCredits.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id)
            .Select(value => value.AmountMinor).ToArray();
        var existingTotal = existing.Sum();
        var total = checked(existingTotal + operation.CreditMinor);
        var frozen = FrozenOrderChargeMath.Read(source, money);
        if (operation.CreditMinor <= 0 || money.ToMinor(source.BillingCreditAmount) != existingTotal
            || total > frozen.FoodMinor
            || context.OrderBillingCredits.Local.Any(value => value.AmendmentId == amendment.Id))
            throw new ConflictException("The amendment credit exceeds the retained frozen food charge.");

        source.BillingCreditAmount = money.ToMajor(total);
        context.OrderBillingCredits.Add(new OrderBillingCredit
        {
            Id = Guid.NewGuid(),
            SourceOrderId = source.Id,
            AmendmentId = amendment.Id,
            AmountMinor = operation.CreditMinor,
            Currency = operation.Currency,
            ActorUserId = amendment.ActorUserId,
            ActorRole = amendment.ActorRole,
            CreatedAt = now,
            CreatedBy = audit
        });
    }

    private OrderAmendmentResolutionResultDto CreateResult(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyResultDto? loyalty)
    {
        var legs = operation.Legs.ToArray();
        var legIds = legs.Select(value => value.Id).ToHashSet();
        var evidence = context.OrderAmendmentRefundEvidence.Local
            .Where(value => legIds.Contains(value.RefundLegId)).ToArray();
        return OrderAmendmentResolutionResultMapper.Map(operation, legs, evidence, loyalty);
    }
}
