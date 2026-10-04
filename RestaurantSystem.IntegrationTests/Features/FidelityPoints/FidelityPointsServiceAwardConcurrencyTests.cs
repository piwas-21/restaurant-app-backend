using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task AwardPointsAsync_ExactRetryReturnsOriginalAndCredits120Once()
    {
        var orderId = await SeedOrderAsync(_testUserId);

        var original = await _service.AwardPointsAsync(_testUserId, orderId, 120, 60m);
        var retry = await _service.AwardPointsAsync(_testUserId, orderId, 120, 60m);

        Assert.Equal(original.Id, retry.Id);
        Assert.Equal(_testUserId, retry.UserId);
        Assert.Equal(orderId, retry.OrderId);
        Assert.Equal(120, retry.Points);
        Assert.Equal(60m, retry.OrderTotal);
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
        var orderId = await SeedOrderAsync(_testUserId);
        await _service.AwardPointsAsync(_testUserId, orderId, 80, 40m);
        var trackedAward = await _context.FidelityPointsTransactions.SingleAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned);
        trackedAward.Points = 999;

        var replay = await _service.AwardPointsAsync(_testUserId, orderId, 80, 40m);

        Assert.Equal(80, replay.Points);
        Assert.Equal(999, trackedAward.Points);
        Assert.Equal(80, await _context.FidelityPointBalances.AsNoTracking()
            .Where(value => value.UserId == _testUserId)
            .Select(value => value.CurrentPoints)
            .SingleAsync());
    }

    [Fact]
    public async Task AwardPointsAsync_ChangedRetryAndDuplicateHistoryAreHeld()
    {
        var orderId = await SeedOrderAsync(_testUserId);
        await _service.AwardPointsAsync(_testUserId, orderId, 120, 60m);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AwardPointsAsync(_testUserId, orderId, 121, 60m));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AwardPointsAsync(_testUserId, orderId, 120, 61m));

        var duplicateUserId = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(_context, duplicateUserId);
        var duplicateOrderId = await SeedOrderAsync(duplicateUserId);
        _context.FidelityPointsTransactions.AddRange(
            EarnedTransaction(duplicateUserId, duplicateOrderId, 120, 60m),
            EarnedTransaction(duplicateUserId, duplicateOrderId, 120, 60m));
        // The duplicate rows have no balance; history ambiguity must be rejected before repair.
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AwardPointsAsync(duplicateUserId, duplicateOrderId, 120, 60m));

        var retainedBalance = await _context.FidelityPointBalances.AsNoTracking()
            .SingleAsync(value => value.UserId == _testUserId);
        Assert.Equal(120, retainedBalance.CurrentPoints);
        Assert.Equal(120, retainedBalance.TotalEarnedPoints);
        Assert.Equal(2, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == duplicateOrderId && value.TransactionType == TransactionType.Earned));
        Assert.Equal(0, await _context.FidelityPointBalances.CountAsync(value =>
            value.UserId == duplicateUserId));

        var ownerMismatchOrderId = await SeedOrderAsync(_testUserId);
        var wrongOwnerId = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(_context, wrongOwnerId);
        _context.FidelityPointsTransactions.Add(
            EarnedTransaction(wrongOwnerId, ownerMismatchOrderId, 120, 60m));
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AwardPointsAsync(_testUserId, ownerMismatchOrderId, 120, 60m));
        Assert.Equal(120, await _context.FidelityPointBalances.AsNoTracking()
            .Where(value => value.UserId == _testUserId)
            .Select(value => value.CurrentPoints)
            .SingleAsync());
    }

    [Fact]
    public async Task AwardPointsAsync_ConcurrentSameOrderRetryCreatesOneAward()
    {
        var orderId = await SeedOrderAsync(_testUserId);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = CreateService(firstContext);
        var second = CreateService(secondContext);

        var results = await Task.WhenAll(
            first.AwardPointsAsync(_testUserId, orderId, 120, 60m),
            second.AwardPointsAsync(_testUserId, orderId, 120, 60m));

        Assert.Equal(results[0].Id, results[1].Id);
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
        var firstOrderId = await SeedOrderAsync(_testUserId);
        var secondOrderId = await SeedOrderAsync(_testUserId);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = CreateService(firstContext);
        var second = CreateService(secondContext);

        await Task.WhenAll(
            first.AwardPointsAsync(_testUserId, firstOrderId, 70, 35m),
            second.AwardPointsAsync(_testUserId, secondOrderId, 50, 25m));

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
        var firstOrderId = await SeedOrderAsync(_testUserId);
        var secondOrderId = await SeedOrderAsync(_testUserId);
        var thirdOrderId = await SeedOrderAsync(_testUserId);
        await _service.AwardPointsAsync(_testUserId, firstOrderId, 20, 10m);

        await using (var secondContext = _fixture.CreateContext())
        {
            await CreateService(secondContext).AwardPointsAsync(_testUserId, secondOrderId, 50, 25m);
        }

        await _service.AwardPointsAsync(_testUserId, thirdOrderId, 50, 25m);

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
        var orderId = await SeedOrderAsync(userId);
        var accountOwner = await _context.Users.SingleAsync(value => value.Id == userId);
        accountOwner.IsDeleted = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AwardPointsAsync(userId, orderId, 20, 10m));

        Assert.Equal(0, await _context.FidelityPointsTransactions.CountAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned));
        Assert.Equal(0, await _context.FidelityPointBalances.CountAsync(value => value.UserId == userId));
        Assert.True(accountOwner.IsDeleted);
    }

    [Fact]
    public async Task AwardPointsAsync_OverflowInsideCallerTransactionLeavesNoTrackedPartialChange()
    {
        var orderId = await SeedOrderAsync(_testUserId);
        var balance = CreateBalance(_testUserId, currentPoints: 7, totalEarnedPoints: int.MaxValue);
        _context.FidelityPointBalances.Add(balance);
        await _context.SaveChangesAsync();

        await using var callerTransaction = await _context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<OverflowException>(() =>
            _service.AwardPointsAsync(_testUserId, orderId, 1, 1m));

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
        var firstOrderId = await SeedOrderAsync(_testUserId);
        var secondOrderId = await SeedOrderAsync(_testUserId);
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
            Guid orderId,
            int points,
            decimal total)
        {
            await CreateService(context).AwardPointsAsync(_testUserId, orderId, points, total, timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }

        await Task.WhenAll(
            AwardAndCommitAsync(firstContext, firstTransaction, firstOrderId, 70, 35m),
            AwardAndCommitAsync(secondContext, secondTransaction, secondOrderId, 50, 25m))
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
