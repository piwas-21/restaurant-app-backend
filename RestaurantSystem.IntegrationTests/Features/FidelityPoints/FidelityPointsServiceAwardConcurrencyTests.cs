using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task AwardPointsAsync_ExactRetryReturnsOriginalAndCredits120Once()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 120, 60m);

        var original = await _service.AwardAcceptedOrderAsync(orderId);
        var retry = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.Awarded, original.Disposition);
        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, retry.Disposition);
        Assert.Equal<int?>(120, retry.CandidatePoints);
        Assert.Equal<int?>(120, retry.AppliedPoints);
        Assert.Equal<int?>(0, retry.SuppressedPoints);
        Assert.Equal(1, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));

        var balance = await _context.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, balance.CurrentPoints);
        Assert.Equal(120, balance.TotalEarnedPoints);
    }

    [Fact]
    public async Task AwardPointsAsync_ExactRetryUsesPersistedHistoryOverModifiedTrackedCopy()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m);
        await _service.AwardAcceptedOrderAsync(orderId);
        var trackedAward = await _context.FidelityPointsTransactions.SingleAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned);
        trackedAward.Points = 999;

        var replay = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, replay.Disposition);
        Assert.Equal<int?>(80, replay.AppliedPoints);
        Assert.Equal(999, trackedAward.Points);
        Assert.Equal(80, await _context.FidelityPointBalances.AsNoTracking()
            .Where(value => value.UserId == _testUserId)
            .Select(value => value.CurrentPoints)
            .SingleAsync());
    }

    [Fact]
    public async Task AwardPointsAsync_DatabaseRetainsFrozenPreviewAndServiceHoldsDuplicateHistory()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 120, 60m);
        await _service.AwardAcceptedOrderAsync(orderId);
        var acceptedOrder = await _context.Orders.SingleAsync(value => value.Id == orderId);
        acceptedOrder.FidelityPointsEarned = 121;
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.GetBaseException());
        Assert.Equal("23514", postgres.SqlState);
        Assert.Equal("Accepted order billing facts are immutable after snapshot creation", postgres.MessageText);
        _context.Entry(acceptedOrder).State = EntityState.Detached;

        await using (var verifyContext = _fixture.CreateContext())
        {
            var persistedPreview = await verifyContext.Orders.AsNoTracking()
                .Where(value => value.Id == orderId)
                .Select(value => value.FidelityPointsEarned)
                .SingleAsync();
            Assert.Equal(120, persistedPreview);
            var replay = await CreateService(verifyContext).AwardAcceptedOrderAsync(orderId);
            Assert.Equal(FidelityPointsAwardDisposition.AlreadyAwarded, replay.Disposition);
            Assert.Equal<int?>(120, replay.AppliedPoints);
        }

        await using var legacy = await PublishedBillingParentDatabase.CreateAsync();
        var duplicateUserId = Guid.NewGuid();
        var wrongOwnerId = Guid.NewGuid();
        Guid duplicateOrderId;
        Guid ownerMismatchOrderId;
        await using (var legacyContext = legacy.CreateContext())
        {
            duplicateOrderId = await SeedLegacyAcceptedAwardOrderAsync(legacyContext,
                duplicateUserId, 120, 60m);
            ownerMismatchOrderId = await SeedLegacyAcceptedAwardOrderAsync(legacyContext,
                _testUserId, 120, 60m);
            await TestUserSeeder.SeedUserAsync(legacyContext, wrongOwnerId);
        }

        var legacyAt = DateTime.UtcNow;
        await legacy.InsertEarnedTransactionAsync(Guid.NewGuid(), duplicateUserId, duplicateOrderId,
            120, 60m, legacyAt);
        await legacy.InsertEarnedTransactionAsync(Guid.NewGuid(), duplicateUserId, duplicateOrderId,
            120, 60m, legacyAt.AddTicks(1));
        await legacy.InsertEarnedTransactionAsync(Guid.NewGuid(), wrongOwnerId, ownerMismatchOrderId,
            120, 60m, legacyAt);
        await legacy.UpgradeToCurrentAsync();

        await using var legacyCurrent = legacy.CreateContext();
        var legacyService = CreateService(legacyCurrent);
        // Duplicate rows have no balance; history ambiguity is rejected before repair.
        await Assert.ThrowsAsync<ConflictException>(() => legacyService.AwardAcceptedOrderAsync(duplicateOrderId));

        Assert.Equal(2, await legacyCurrent.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == duplicateOrderId && value.TransactionType == TransactionType.Earned));
        Assert.Equal(0, await legacyCurrent.FidelityPointBalances.CountAsync(value =>
            value.UserId == duplicateUserId));

        await Assert.ThrowsAsync<ConflictException>(() =>
            legacyService.AwardAcceptedOrderAsync(ownerMismatchOrderId));
        var primaryBalance = await _context.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, primaryBalance.CurrentPoints);
        Assert.Equal(120, primaryBalance.TotalEarnedPoints);
    }

    [Fact]
    public async Task AwardPointsAsync_ConcurrentSameOrderRetryCreatesOneAward()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 120, 60m);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = CreateService(firstContext);
        var second = CreateService(secondContext);

        var results = await Task.WhenAll(
            first.AwardAcceptedOrderAsync(orderId),
            second.AwardAcceptedOrderAsync(orderId));

        Assert.Equal(1, results.Count(result => result.Disposition == FidelityPointsAwardDisposition.Awarded));
        Assert.Equal(1, results.Count(result => result.Disposition == FidelityPointsAwardDisposition.AlreadyAwarded));
        await using var verifyContext = _fixture.CreateContext();
        Assert.Equal(1, await verifyContext.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        var balance = await verifyContext.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, balance.CurrentPoints);
        Assert.Equal(120, balance.TotalEarnedPoints);
    }

    [Fact]
    public async Task AwardPointsAsync_ConcurrentFirstAwardsForUserDoNotLoseBalance()
    {
        var firstOrderId = await SeedAwardOrderAsync(_testUserId, 70, 35m);
        var secondOrderId = await SeedAwardOrderAsync(_testUserId, 50, 25m);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = CreateService(firstContext);
        var second = CreateService(secondContext);

        await Task.WhenAll(
            first.AwardAcceptedOrderAsync(firstOrderId),
            second.AwardAcceptedOrderAsync(secondOrderId));

        await using var verifyContext = _fixture.CreateContext();
        var balance = await verifyContext.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, balance.CurrentPoints);
        Assert.Equal(120, balance.TotalEarnedPoints);
        Assert.Equal(2, await verifyContext.FidelityPointsTransactions.CountAsync(value =>
            value.UserId == _testUserId && value.TransactionType == TransactionType.Earned));
    }

    [Fact]
    public async Task AwardPointsAsync_RefreshesPretrackedBalanceAfterAnotherContextAward()
    {
        var firstOrderId = await SeedAwardOrderAsync(_testUserId, 20, 10m);
        var secondOrderId = await SeedAwardOrderAsync(_testUserId, 50, 25m);
        var thirdOrderId = await SeedAwardOrderAsync(_testUserId, 50, 25m);
        await _service.AwardAcceptedOrderAsync(firstOrderId);

        await using (var secondContext = _fixture.CreateContext())
        {
            await CreateService(secondContext).AwardAcceptedOrderAsync(secondOrderId);
        }

        await _service.AwardAcceptedOrderAsync(thirdOrderId);

        var balance = await _context.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, balance.CurrentPoints);
        Assert.Equal(120, balance.TotalEarnedPoints);
    }

    [Fact]
    public async Task AwardPointsAsync_HoldsSoftDeletedAccountOwner()
    {
        var userId = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(_context, userId);
        var orderId = await SeedAwardOrderAsync(userId, 20, 10m);
        var accountOwner = await _context.Users.SingleAsync(value => value.Id == userId);
        accountOwner.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await _service.AwardAcceptedOrderAsync(orderId);

        Assert.Equal(FidelityPointsAwardDisposition.Deferred, result.Disposition);
        Assert.Equal<FidelityPointsAwardDeferralReason?>(
            FidelityPointsAwardDeferralReason.EarningOwnerUnavailable, result.DeferralReason);
        Assert.False(await _context.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == orderId));
        Assert.Equal(0, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        Assert.Equal(0, await _context.FidelityPointBalances.CountAsync(value => value.UserId == userId));
        Assert.True(accountOwner.IsDeleted);
    }

    [Fact]
    public async Task AwardPointsAsync_OverflowInsideCallerTransactionLeavesNoTrackedPartialChange()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 1, 1m);
        var balance = CreateBalance(_testUserId, currentPoints: 7, totalEarnedPoints: int.MaxValue);
        _context.FidelityPointBalances.Add(balance);
        await _context.SaveChangesAsync();

        await using var callerTransaction = await _context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<OverflowException>(() => _service.AwardAcceptedOrderAsync(orderId));

        Assert.Equal(7, balance.CurrentPoints);
        Assert.Equal(int.MaxValue, balance.TotalEarnedPoints);
        Assert.Equal(EntityState.Unchanged, _context.Entry(balance).State);

        await _context.SaveChangesAsync();
        await callerTransaction.CommitAsync();

        await using var verifyContext = _fixture.CreateContext();
        var persisted = await verifyContext.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(7, persisted.CurrentPoints);
        Assert.Equal(int.MaxValue, persisted.TotalEarnedPoints);
        Assert.Equal(0, await verifyContext.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
    }

    [Fact]
    public async Task AwardPointsAsync_ConcurrentCallerTransactionsWithUserForeignKeyLocksDoNotDeadlock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstOrderId = await SeedAwardOrderAsync(_testUserId, 70, 35m);
        var secondOrderId = await SeedAwardOrderAsync(_testUserId, 50, 25m);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(timeout.Token);
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(timeout.Token);

        // An order insert obtains KEY SHARE on its referenced User row. The order-change journal
        // trigger serializes uncommitted order writes with a global advisory lock, so acquire the
        // same FK row lock directly here after seeding distinct orders outside these transactions.
        var firstLockedUser = await firstContext.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM \"Users\" WHERE id = {_testUserId} FOR KEY SHARE")
            .SingleAsync(timeout.Token);
        var secondLockedUser = await secondContext.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM \"Users\" WHERE id = {_testUserId} FOR KEY SHARE")
            .SingleAsync(timeout.Token);
        Assert.Equal(_testUserId, firstLockedUser);
        Assert.Equal(_testUserId, secondLockedUser);

        async Task AwardAndCommitAsync(
            ApplicationDbContext context,
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
            Guid orderId)
        {
            await CreateService(context).AwardAcceptedOrderAsync(orderId, timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }

        await Task.WhenAll(
            AwardAndCommitAsync(firstContext, firstTransaction, firstOrderId),
            AwardAndCommitAsync(secondContext, secondTransaction, secondOrderId))
            .WaitAsync(timeout.Token);

        await using var verifyContext = _fixture.CreateContext();
        var balance = await verifyContext.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, balance.CurrentPoints);
        Assert.Equal(120, balance.TotalEarnedPoints);
        Assert.Equal(2, await verifyContext.FidelityPointsTransactions.CountAsync(value =>
            value.UserId == _testUserId && value.TransactionType == TransactionType.Earned));
    }

    private FidelityPointsService CreateService(ApplicationDbContext context) =>
        new(context, _currentUserServiceMock.Object, _ruleServiceMock.Object);

    private static FidelityPointsTransaction EarnedTransaction(
        Guid userId, Guid orderId, int points, decimal orderTotal)
    {
        var now = DateTime.UtcNow;
        return new FidelityPointsTransaction
        {
            UserId = userId,
            OrderId = orderId,
            TransactionType = TransactionType.Earned,
            Points = points,
            OrderTotal = orderTotal,
            CreatedAt = now,
            CreatedBy = userId.ToString()
        };
    }

    private static FidelityPointBalance CreateBalance(
        Guid userId,
        int currentPoints,
        int? totalEarnedPoints = null)
    {
        var now = DateTime.UtcNow;
        return new FidelityPointBalance
        {
            UserId = userId,
            CurrentPoints = currentPoints,
            TotalEarnedPoints = totalEarnedPoints ?? currentPoints,
            TotalRedeemedPoints = 0,
            LastUpdated = now,
            CreatedAt = now,
            CreatedBy = userId.ToString()
        };
    }
}
