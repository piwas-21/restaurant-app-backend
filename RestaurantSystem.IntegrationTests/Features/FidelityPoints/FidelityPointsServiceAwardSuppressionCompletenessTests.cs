using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task AwardAcceptedOrderAsync_HoldsWhenCommittedPositiveRemovalLacksSuppression()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var units = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.UnitOrdinal)
            .ToListAsync();
        var firstAmendment = CreateCommittedVoid(orderId, Guid.NewGuid(), units[0].OrderItemId,
            units[0].UnitOrdinal, 1);
        var secondAmendment = CreateCommittedVoid(orderId, Guid.NewGuid(), units[1].OrderItemId,
            units[1].UnitOrdinal, 1);
        _context.OrderAmendments.AddRange(firstAmendment, secondAmendment);
        await _context.SaveChangesAsync();

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await new OrderBillingAwardSuppressionWriter(_context)
                .RecordRemovedUnitsAsync(orderId, firstAmendment.Id, CancellationToken.None);
            await transaction.CommitAsync();
        }

        await Assert.ThrowsAsync<ConflictException>(() => _service.AwardAcceptedOrderAsync(orderId));

        Assert.Equal(1, await _context.OrderBillingUnitAwardSuppressions.CountAsync(
            value => value.OrderId == orderId));
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        Assert.False(await _context.FidelityPointBalances.AnyAsync(value => value.UserId == _testUserId));
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_HoldsUnwitnessedEarnedHistoryWhenRemovalTimingIsAmbiguous()
    {
        await using var legacy = await PublishedBillingParentDatabase.CreateAsync();
        Guid orderId;
        var transactionId = Guid.NewGuid();
        var earnedAt = DateTime.UtcNow;
        await using (var context = legacy.CreateContext())
        {
            orderId = await SeedLegacyAcceptedAwardOrderAsync(context, _testUserId, 80, 40m, quantity: 2);
            var unit = await context.OrderBillingSnapshotUnits.AsNoTracking()
                .Where(value => value.OrderId == orderId)
                .OrderBy(value => value.UnitOrdinal)
                .FirstAsync();
            var sourceItem = await context.OrderItems.SingleAsync(value => value.Id == unit.OrderItemId);
            context.OrderAmendments.Add(CreateCommittedVoid(orderId, Guid.NewGuid(), unit.OrderItemId,
                unit.UnitOrdinal, 1, sourceItem));
            context.FidelityPointBalances.Add(CreateBalance(_testUserId, 80, 80));
            await context.SaveChangesAsync();
        }

        await legacy.InsertEarnedTransactionAsync(transactionId, _testUserId, orderId, 80, 40m, earnedAt);
        await legacy.UpgradeToCurrentAsync();

        await using var current = legacy.CreateContext();
        var service = CreateService(current);
        await Assert.ThrowsAsync<ConflictException>(() => service.AwardAcceptedOrderAsync(orderId));

        Assert.False(await current.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.Equal(1, await current.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        var balance = await current.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(80, balance.CurrentPoints);
        Assert.Equal(80, balance.TotalEarnedPoints);
    }

    [Fact]
    public async Task AwardAcceptedOrderAsync_BackfillsExactPositiveHistoryWithoutRemovalAndReplaysOnce()
    {
        await using var legacy = await PublishedBillingParentDatabase.CreateAsync();
        Guid orderId;
        var transactionId = Guid.NewGuid();
        var earnedAt = DateTime.UtcNow;
        await using (var context = legacy.CreateContext())
        {
            orderId = await SeedLegacyAcceptedAwardOrderAsync(context, _testUserId, 80, 40m);
            context.FidelityPointBalances.Add(CreateBalance(_testUserId, 80, 80));
            await context.SaveChangesAsync();
        }

        await legacy.InsertEarnedTransactionAsync(transactionId, _testUserId, orderId, 80, 40m, earnedAt);
        await legacy.UpgradeToCurrentAsync();

        await using var current = legacy.CreateContext();
        var service = CreateService(current);
        var backfill = await service.AwardAcceptedOrderAsync(orderId);
        var retry = await service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, backfill.Disposition);
        Assert.Equal<int?>(80, backfill.CandidatePoints);
        Assert.Equal<int?>(80, backfill.AppliedPoints);
        Assert.Equal<int?>(0, backfill.SuppressedPoints);
        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, retry.Disposition);
        Assert.Equal<int?>(80, retry.AppliedPoints);
        var witness = await current.OrderBillingAwardWitnesses.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        Assert.Equal(OrderBillingAwardOutcome.Awarded, witness.Outcome);
        Assert.Equal(80, witness.CandidatePoints);
        Assert.Equal(80, witness.AppliedPoints);
        Assert.Equal(0, witness.SuppressedPoints);
        Assert.Equal(transactionId, witness.EarnedTransactionId);
        Assert.Equal(1, await current.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        var balance = await current.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(80, balance.CurrentPoints);
        Assert.Equal(80, balance.TotalEarnedPoints);
    }
}
