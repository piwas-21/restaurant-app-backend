using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task Earned_clawback_reservation_cannot_be_spent_or_removed_by_adjustment()
    {
        var sourceOrderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        await _service.AwardAcceptedOrderAsync(sourceOrderId);
        var debitOrderId = await SeedOrderAsync(_testUserId);
        var earned = await _context.FidelityPointsTransactions.SingleAsync(value =>
            value.OrderId == sourceOrderId && value.TransactionType == TransactionType.Earned);
        var witness = await _context.OrderBillingAwardWitnesses.SingleAsync(value =>
            value.OrderId == sourceOrderId);
        var unit = await _context.OrderBillingSnapshotUnits.Where(value => value.OrderId == sourceOrderId)
            .OrderBy(value => value.UnitOrdinal).FirstAsync();
        var owner = await _context.OrderBillingSnapshotOwnerLinks.SingleAsync(value =>
            value.OrderId == sourceOrderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning);
        var amendment = CreateCommittedVoid(sourceOrderId, Guid.NewGuid(), unit.OrderItemId,
            unit.UnitOrdinal, 1);
        _context.OrderAmendments.Add(amendment);
        var now = DateTime.UtcNow;
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = Guid.NewGuid(),
            AmendmentId = amendment.Id,
            SourceOrderId = sourceOrderId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = _testUserId,
            ActorRole = "Admin",
            Currency = "CHF",
            CreditMinor = 1,
            RefundMinor = 0,
            UnpaidWaivedMinor = 1,
            RequestHash = new string('a', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.Processing,
            StartedAt = now,
            CreatedAt = now,
            CreatedBy = "LoyaltyReservationTest"
        };
        _context.OrderAmendmentResolutionOperations.Add(operation);
        await _context.SaveChangesAsync();

        var compensation = new OrderAmendmentLoyaltyCompensation
        {
            Id = Guid.NewGuid(),
            SourceOrderId = sourceOrderId,
            AmendmentId = amendment.Id,
            SnapshotId = (await _context.OrderBillingSnapshots.SingleAsync(value =>
                value.OrderId == sourceOrderId)).Id,
            OwnerLinkId = owner.Id,
            OriginalTransactionId = earned.Id,
            AwardWitnessId = witness.Id,
            OperationId = operation.Id,
            Kind = OrderAmendmentLoyaltyCompensationKind.EarnedClawback,
            OriginalTransactionPoints = earned.Points,
            RequiredPoints = unit.EarnedPoints,
            PlanFingerprint = new string('b', 64),
            CreatedAt = now,
            CreatedBy = "LoyaltyReservationTest"
        };
        _context.OrderAmendmentLoyaltyCompensations.Add(compensation);
        _context.OrderAmendmentLoyaltyCompensationUnits.Add(new OrderAmendmentLoyaltyCompensationUnit
        {
            Id = Guid.NewGuid(),
            CompensationId = compensation.Id,
            SourceOrderId = sourceOrderId,
            SnapshotUnitId = unit.Id,
            Kind = compensation.Kind,
            Points = unit.EarnedPoints,
            CreatedAt = now,
            CreatedBy = "LoyaltyReservationTest"
        });
        _context.OrderAmendmentLoyaltyReservations.Add(new OrderAmendmentLoyaltyReservation
        {
            Id = Guid.NewGuid(),
            SourceOrderId = sourceOrderId,
            OperationId = operation.Id,
            CompensationId = compensation.Id,
            OwnerLinkId = owner.Id,
            State = OrderAmendmentLoyaltyReservationState.Reserved,
            CreatedAt = now,
            CreatedBy = "LoyaltyReservationTest"
        });
        await _context.SaveChangesAsync();

        var insufficient = await Assert.ThrowsAsync<InsufficientPointsException>(
            () => _service.RedeemPointsAsync(_testUserId, debitOrderId, 41));
        Assert.Contains("Available: 40", insufficient.Message);
        var redeemed = await _service.RedeemPointsAsync(_testUserId, debitOrderId, 40);
        Assert.Equal(-40, redeemed.Transaction.Points);
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.AdjustPointsAsync(_testUserId, -1, "Reserved loyalty points"));

        Assert.Equal(40, (await _service.GetUserBalanceAsync(_testUserId))!.CurrentPoints);
        Assert.Equal(40, await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
            _context, _testUserId, CancellationToken.None));
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == debitOrderId && value.TransactionType == TransactionType.AdminAdjustment));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_RecordsEvaluatedZeroButHoldsUnevaluatedAndMissingEvidence()
    {
        var noMatchOrderId = await SeedAwardOrderAsync(_testUserId, 0, 20m);
        var matchedZeroOrderId = await SeedAwardOrderAsync(
            _testUserId, 0, 20m, matchedZeroPointRule: true);
        var unevaluatedOrderId = await SeedAwardOrderAsync(
            _testUserId, null, 20m, evaluated: false);
        var legacyOrderId = await SeedAwardOrderAsync(
            _testUserId, null, 20m, includeSnapshot: false, evaluated: false);

        var noMatch = await _service.AwardAcceptedOrderAsync(noMatchOrderId);
        var matchedZero = await _service.AwardAcceptedOrderAsync(matchedZeroOrderId);
        var unevaluated = await _service.AwardAcceptedOrderAsync(unevaluatedOrderId);
        var missing = await _service.AwardAcceptedOrderAsync(legacyOrderId);

        Assert.Equal(FidelityPointsAwardDisposition.EvaluatedZero, noMatch.Disposition);
        Assert.Equal(FidelityPointsAwardDisposition.EvaluatedZero, matchedZero.Disposition);
        Assert.Equal<FidelityPointsAwardDeferralReason?>(
            FidelityPointsAwardDeferralReason.CandidateUnevaluated, unevaluated.DeferralReason);
        Assert.Equal(FidelityPointsAwardDisposition.Deferred, unevaluated.Disposition);
        Assert.Null(unevaluated.CandidatePoints);
        Assert.Null(unevaluated.AppliedPoints);
        Assert.Null(unevaluated.SuppressedPoints);
        Assert.Equal<FidelityPointsAwardDeferralReason?>(
            FidelityPointsAwardDeferralReason.MissingSnapshot, missing.DeferralReason);
        Assert.Equal(FidelityPointsAwardDisposition.Deferred, missing.Disposition);
        Assert.Null(missing.CandidatePoints);
        Assert.Equal(2, await _context.OrderBillingAwardWitnesses.CountAsync(value =>
            value.OrderId == noMatchOrderId || value.OrderId == matchedZeroOrderId));
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value =>
            value.OrderId == unevaluatedOrderId || value.OrderId == legacyOrderId));
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            (value.OrderId == noMatchOrderId || value.OrderId == matchedZeroOrderId
                || value.OrderId == unevaluatedOrderId || value.OrderId == legacyOrderId)
            && value.TransactionType == TransactionType.Earned));

        var noMatchWitness = await _context.OrderBillingAwardWitnesses.SingleAsync(value =>
            value.OrderId == noMatchOrderId);
        Assert.Equal(OrderBillingAwardOutcome.EvaluatedZero, noMatchWitness.Outcome);
        Assert.Equal(0, noMatchWitness.CandidatePoints);
        Assert.Null(noMatchWitness.EarnedTransactionId);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Completed, FidelityPointsAwardDeferralReason.OrderCancelled)]
    [InlineData(OrderStatus.Confirmed, PaymentStatus.Pending, FidelityPointsAwardDeferralReason.PaymentNotSettled)]
    public async Task AwardAcceptedOrderAsync_IneligibleMoneyDoesNotCreateTerminalWitness(
        OrderStatus status, PaymentStatus paymentStatus, FidelityPointsAwardDeferralReason expectedReason)
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 40, 20m);
        var order = await _context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = status;
        order.PaymentStatus = paymentStatus;
        await _context.SaveChangesAsync();

        var result = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.Deferred, result.Disposition);
        Assert.Equal<FidelityPointsAwardDeferralReason?>(expectedReason, result.DeferralReason);
        Assert.Null(result.CandidatePoints);
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        Assert.False(await _context.FidelityPointBalances.AnyAsync(value => value.UserId == _testUserId));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_ProviderManagedOrderIsHeldWithoutWitness()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 40, 20m);
        var unit = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        _context.OrderAmendments.Add(CreateCommittedVoid(orderId, Guid.NewGuid(), unit.OrderItemId,
            unit.UnitOrdinal, 1));
        _context.ExternalOrderReferences.Add(new ExternalOrderReference
        {
            OrderId = orderId,
            Provider = "Synthetic",
            ExternalStoreId = "fixture-store",
            ExternalOrderId = Guid.NewGuid().ToString("N"),
            ExternalDisplayId = "fixture-order",
            ExternalState = "accepted",
            LastEventAt = DateTime.UtcNow,
            Currency = "CHF",
            MerchantTotal = 20m,
            PayloadHash = new string('a', 64),
            FulfillmentType = "pickup",
            IsSandbox = true,
            CreatedBy = "AwardSnapshotTest"
        });
        await _context.SaveChangesAsync();

        var result = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.Deferred, result.Disposition);
        Assert.Equal<FidelityPointsAwardDeferralReason?>(
            FidelityPointsAwardDeferralReason.ProviderManaged, result.DeferralReason);
        Assert.Null(result.CandidatePoints);
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_PreAwardRemovalSuppressesOnlyFrozenUnitsAndReplaysExactly()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 120, 60m, quantity: 2);
        var units = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.UnitOrdinal)
            .ToListAsync();
        var removed = units[0];
        var amendmentId = Guid.NewGuid();
        _context.OrderAmendments.Add(CreateCommittedVoid(orderId, amendmentId, removed.OrderItemId,
            removed.UnitOrdinal, 1));
        await _context.SaveChangesAsync();

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            var writer = new OrderBillingAwardSuppressionWriter(_context);
            await writer.RecordRemovedUnitsAsync(orderId, amendmentId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        var originalPreview = await _context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId)
            .Select(value => value.FidelityPointsEarned)
            .SingleAsync();
        var first = await _service.AwardAcceptedOrderAsync(orderId);
        var retry = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(120, originalPreview);
        Assert.Equal(FidelityPointsAwardDisposition.Awarded, first.Disposition);
        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, retry.Disposition);
        Assert.Equal<int?>(120, first.CandidatePoints);
        Assert.Equal<int?>(removed.EarnedPoints, first.SuppressedPoints);
        Assert.Equal<int?>(120 - removed.EarnedPoints, first.AppliedPoints);
        Assert.Equal(first.AppliedPoints, retry.AppliedPoints);
        Assert.Equal(1, await _context.OrderBillingUnitAwardSuppressions.CountAsync(value => value.OrderId == orderId));
        Assert.Equal(1, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        var balance = await _context.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(first.AppliedPoints, (int?)balance.CurrentPoints);
        Assert.Equal(first.AppliedPoints, (int?)balance.TotalEarnedPoints);
        var witness = await _context.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        Assert.Equal(OrderBillingAwardOutcome.Awarded, witness.Outcome);
        Assert.Equal(removed.EarnedPoints, witness.SuppressedPoints);
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_FullyRemovedCandidateRecordsNoAwardWitness()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 40, 20m);
        var unit = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        var amendmentId = Guid.NewGuid();
        _context.OrderAmendments.Add(CreateCommittedVoid(orderId, amendmentId, unit.OrderItemId,
            unit.UnitOrdinal, 1));
        await _context.SaveChangesAsync();
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await new OrderBillingAwardSuppressionWriter(_context)
                .RecordRemovedUnitsAsync(orderId, amendmentId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        var first = await _service.AwardAcceptedOrderAsync(orderId);
        var retry = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.FullySuppressed, first.Disposition);
        Assert.Equal(FidelityPointsAwardDisposition.FullySuppressed, retry.Disposition);
        Assert.Equal<int?>(40, first.CandidatePoints);
        Assert.Equal<int?>(0, first.AppliedPoints);
        Assert.Equal<int?>(40, first.SuppressedPoints);
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        Assert.False(await _context.FidelityPointBalances.AnyAsync(value => value.UserId == _testUserId));
        var witness = await _context.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        Assert.Equal(OrderBillingAwardOutcome.FullySuppressed, witness.Outcome);
        Assert.Null(witness.EarnedTransactionId);
    }

    [Fact]
    public async Task SuppressionWriter_RejectsOverlappingCommittedRangesWithoutPartialRows()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var unit = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.UnitOrdinal)
            .FirstAsync();
        var amendment = CreateCommittedVoid(orderId, Guid.NewGuid(), unit.OrderItemId,
            unit.UnitOrdinal, 1);
        var repeated = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        amendment.ChangesJson = OrderAmendmentJson.Serialize(new[] { repeated[0], repeated[0] });
        _context.OrderAmendments.Add(amendment);
        await _context.SaveChangesAsync();

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<ConflictException>(() =>
                new OrderBillingAwardSuppressionWriter(_context).RecordRemovedUnitsAsync(
                    orderId, amendment.Id, CancellationToken.None));
        }

        Assert.False(await _context.OrderBillingUnitAwardSuppressions.AnyAsync(value =>
            value.OrderId == orderId));
        Assert.Equal(0, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_AwardFirstDoesNotRetroactivelySuppressPostedLedger()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var removed = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.UnitOrdinal)
            .FirstAsync();
        var awarded = await _service.AwardAcceptedOrderAsync(orderId);
        var amendmentId = Guid.NewGuid();
        _context.OrderAmendments.Add(CreateCommittedVoid(orderId, amendmentId, removed.OrderItemId,
            removed.UnitOrdinal, 1));
        await _context.SaveChangesAsync();
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await new OrderBillingAwardSuppressionWriter(_context)
                .RecordRemovedUnitsAsync(orderId, amendmentId, CancellationToken.None);
            await transaction.CommitAsync();
        }
        var retry = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.Awarded, awarded.Disposition);
        Assert.Equal<int?>(80, awarded.AppliedPoints);
        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, retry.Disposition);
        Assert.Equal<int?>(80, retry.AppliedPoints);
        Assert.False(await _context.OrderBillingUnitAwardSuppressions.AnyAsync(value => value.OrderId == orderId));
        Assert.Equal(1, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_ExactPersistedLegacyAwardReplaysWithoutInventedSnapshot()
    {
        var orderId = await SeedOrderAsync(_testUserId);
        var order = await _context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Completed;
        order.FidelityPointsEarned = 120;
        order.SubTotal = 60m;
        _context.FidelityPointsTransactions.Add(EarnedTransaction(_testUserId, orderId, 120, 60m));
        _context.FidelityPointBalances.Add(CreateBalance(_testUserId, 120, 120));
        await _context.SaveChangesAsync();

        var result = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, result.Disposition);
        Assert.Equal<int?>(120, result.CandidatePoints);
        Assert.Equal<int?>(120, result.AppliedPoints);
        Assert.False(await _context.OrderBillingSnapshots.AnyAsync(value => value.OrderId == orderId));
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.Equal(1, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
    }

    private OrderAmendment CreateCommittedVoid(
        Guid orderId, Guid amendmentId, Guid orderItemId, int ordinal, int quantity,
        OrderItem? sourceItem = null)
    {
        var now = DateTime.UtcNow;
        sourceItem ??= _context.OrderItems.Local.Single(value => value.Id == orderItemId);
        var change = new OrderAmendmentChangeSnapshot(orderItemId, OrderAmendmentChangeKind.Void,
            ordinal, quantity, false, new OrderItemDto
            {
                Id = orderItemId,
                ProductName = sourceItem.ProductName,
                Quantity = sourceItem.Quantity,
                UnitPrice = sourceItem.UnitPrice,
                ItemTotal = sourceItem.ItemTotal
            }, null);
        return new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = orderId,
            ActorUserId = _testUserId,
            ActorRole = "Admin",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = now.AddMinutes(5),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = now,
            CreatedBy = "AwardSnapshotTest"
        };
    }
}
