using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task AwardJournals_RejectMutationAndPostAwardSuppression()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var result = await _service.AwardAcceptedOrderAsync(orderId);
        Assert.Equal(FidelityPointsAwardDisposition.Awarded, result.Disposition);
        var witness = await _context.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        var earned = await _context.FidelityPointsTransactions.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId && value.TransactionType == TransactionType.Earned);

        var first = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId).OrderBy(value => value.UnitOrdinal).FirstAsync();
        var amendment = CreateCommittedVoid(orderId, Guid.NewGuid(), first.OrderItemId, first.UnitOrdinal, 1);
        _context.OrderAmendments.Add(amendment);
        await _context.SaveChangesAsync();

        _context.OrderBillingUnitAwardSuppressions.Add(NewSuppression(orderId, first, amendment.Id));
        var retroactive = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(retroactive.GetBaseException()).SqlState);
        _context.ChangeTracker.Clear();

        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE order_billing_award_witnesses SET applied_points = applied_points + 1 WHERE id = {witness.Id}"));
        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM order_billing_award_witnesses WHERE id = {witness.Id}"));
        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE fidelity_points_transactions SET points = points + 1 WHERE id = {earned.Id}"));

        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.OrderBillingAwardWitnesses.CountAsync(value => value.Id == witness.Id));
        Assert.Equal(80, await verify.FidelityPointsTransactions.Where(value => value.Id == earned.Id)
            .Select(value => value.Points).SingleAsync());
        Assert.False(await verify.OrderBillingUnitAwardSuppressions.AnyAsync(value => value.OrderId == orderId));
    }

    [Fact]
    public async Task SuppressionInsert_RequiresExactCommittedRemovalScopeAndImmutableSource()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var units = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId).OrderBy(value => value.UnitOrdinal).ToListAsync();
        var amendment = CreateCommittedVoid(orderId, Guid.NewGuid(), units[0].OrderItemId,
            units[0].UnitOrdinal, 1);
        _context.OrderAmendments.Add(amendment);
        await _context.SaveChangesAsync();

        _context.OrderBillingUnitAwardSuppressions.Add(NewSuppression(orderId, units[1], amendment.Id));
        var outOfScope = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(outOfScope.GetBaseException()).SqlState);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await new OrderBillingAwardSuppressionWriter(_context)
            .RecordRemovedUnitsAsync(orderId, amendment.Id, CancellationToken.None);
        await transaction.CommitAsync();
        var suppression = await _context.OrderBillingUnitAwardSuppressions.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);

        const string settledFinancialResolution = "{}";
        const string resolutionAudit = "OrderAmendmentResolutionFinalizer";
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE order_amendments
            SET financial_resolution_json = CAST({settledFinancialResolution} AS jsonb),
                updated_at = transaction_timestamp(), updated_by = {resolutionAudit}
            WHERE id = {amendment.Id}
            """);

        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE order_billing_unit_award_suppressions SET suppressed_earned_points = suppressed_earned_points + 1 WHERE id = {suppression.Id}"));
        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM order_billing_unit_award_suppressions WHERE id = {suppression.Id}"));
        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE order_amendments SET changes_json = '[]'::jsonb WHERE id = {amendment.Id}"));

        Assert.Equal(1, await _context.OrderBillingUnitAwardSuppressions.CountAsync(value => value.Id == suppression.Id));
        var persistedChanges = await _context.OrderAmendments.AsNoTracking()
            .Where(value => value.Id == amendment.Id).Select(value => value.ChangesJson).SingleAsync();
        var expectedChanges = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        var actualChanges = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(persistedChanges);
        Assert.Equal(OrderAmendmentJson.Serialize(expectedChanges), OrderAmendmentJson.Serialize(actualChanges));
    }

    [Fact]
    public async Task AwardWitnessCommit_RejectsIncompleteRemovalCoverage()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var units = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId).OrderBy(value => value.UnitOrdinal).ToListAsync();
        var first = CreateCommittedVoid(orderId, Guid.NewGuid(), units[0].OrderItemId, units[0].UnitOrdinal, 1);
        var second = CreateCommittedVoid(orderId, Guid.NewGuid(), units[1].OrderItemId, units[1].UnitOrdinal, 1);
        _context.OrderAmendments.AddRange(first, second);
        await _context.SaveChangesAsync();

        await using (var suppressionTransaction = await _context.Database.BeginTransactionAsync())
        {
            await new OrderBillingAwardSuppressionWriter(_context)
                .RecordRemovedUnitsAsync(orderId, first.Id, CancellationToken.None);
            await suppressionTransaction.CommitAsync();
        }

        var ownerLinkId = await _context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == orderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning)
            .Select(value => value.Id).SingleAsync();
        var earnedId = Guid.NewGuid();
        await using (var awardTransaction = await _context.Database.BeginTransactionAsync())
        {
            _context.FidelityPointBalances.Add(CreateBalance(_testUserId, 40, 40));
            _context.FidelityPointsTransactions.Add(new FidelityPointsTransaction
            {
                Id = earnedId,
                UserId = _testUserId,
                OrderId = orderId,
                TransactionType = TransactionType.Earned,
                Points = 40,
                OrderTotal = 40m,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "AwardJournalSchemaTest"
            });
            _context.OrderBillingAwardWitnesses.Add(new OrderBillingAwardWitness
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                OwnerLinkId = ownerLinkId,
                Outcome = OrderBillingAwardOutcome.Awarded,
                CandidatePoints = 80,
                AppliedPoints = 40,
                SuppressedPoints = 40,
                EarnedTransactionId = earnedId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "OrderBillingAwardBoundary"
            });
            await _context.SaveChangesAsync();
            var incomplete = await Assert.ThrowsAsync<PostgresException>(() => awardTransaction.CommitAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, incomplete.SqlState);
            Assert.Contains("complete pre-award removal coverage", incomplete.Message);
        }

        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.False(await verify.FidelityPointsTransactions.AnyAsync(value => value.Id == earnedId));
        Assert.False(await verify.FidelityPointBalances.AnyAsync(value => value.UserId == _testUserId));
    }

    [Fact]
    public async Task AwardWitnessCommit_RequiresExactEarnedLedgerBinding()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m);
        var ownerLinkId = await _context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == orderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning)
            .Select(value => value.Id).SingleAsync();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        _context.FidelityPointBalances.Add(CreateBalance(_testUserId, 80, 80));
        _context.OrderBillingAwardWitnesses.Add(new OrderBillingAwardWitness
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OwnerLinkId = ownerLinkId,
            Outcome = OrderBillingAwardOutcome.Awarded,
            CandidatePoints = 80,
            AppliedPoints = 80,
            SuppressedPoints = 0,
            EarnedTransactionId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "OrderBillingAwardBoundary"
        });
        await _context.SaveChangesAsync();
        var missingSource = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, missingSource.SqlState);
        Assert.Contains("exactly one matching earned row", missingSource.Message);
    }

    [Fact]
    public async Task WitnessedEarnedSource_RetainsAnonymizedFinancialMovementThroughOwnerErasure()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m);
        var result = await _service.AwardAcceptedOrderAsync(orderId);
        Assert.Equal(FidelityPointsAwardDisposition.Awarded, result.Disposition);
        var witness = await _context.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);

        await AssertSqlCheckViolationAsync(() => _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM fidelity_points_transactions WHERE id = {witness.EarnedTransactionId}"));
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await new RetainedCustomerDataScrubber(_context).ScrubAsync(_testUserId, CancellationToken.None);
            await _context.FidelityPointBalances.Where(value => value.UserId == _testUserId).ExecuteDeleteAsync();
            await _context.FidelityPointsTransactions.Where(value => value.UserId == _testUserId).ExecuteDeleteAsync();
            await _context.Users.IgnoreQueryFilters().Where(value => value.Id == _testUserId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await using var verify = _fixture.CreateContext();
        var retained = await verify.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        Assert.Equal(witness.EarnedTransactionId, retained.EarnedTransactionId);
        var movement = await verify.FidelityPointsTransactions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == witness.EarnedTransactionId);
        Assert.Null(movement.UserId);
        Assert.Equal(orderId, movement.OrderId);
        Assert.Equal(80, movement.Points);
        Assert.Equal(TransactionType.Earned, movement.TransactionType);
        Assert.Equal("[erased]", movement.Description);
        Assert.Equal("RetainedLoyaltyEvidenceScrubber", movement.CreatedBy);
        Assert.False(await verify.Users.AnyAsync(value => value.Id == _testUserId));
        var ownerLink = await verify.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning);
        Assert.Equal(OrderBillingSnapshotOwnerDisposition.Erased, ownerLink.Disposition);
        Assert.Null(ownerLink.UserId);
        Assert.NotNull(ownerLink.ErasureTransactionId);
    }

    private static OrderBillingUnitAwardSuppression NewSuppression(
        Guid orderId, OrderBillingSnapshotUnit unit, Guid amendmentId) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SnapshotUnitId = unit.Id,
            AmendmentId = amendmentId,
            SuppressedEarnedPoints = unit.EarnedPoints,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "OrderBillingAwardSuppression"
        };

    private static async Task AssertSqlCheckViolationAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
