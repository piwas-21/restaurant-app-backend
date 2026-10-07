using Moq;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.FidelityPoints.Models;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

[Collection("Database Lane 3")]
public partial class FidelityPointsServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private FidelityPointsService _service = null!;
    private Mock<IPointEarningRuleService> _ruleServiceMock = null!;
    private Mock<ICurrentUserService> _currentUserServiceMock = null!;
    private Guid _testUserId;
    private string _testAuditIdentifier = string.Empty;

    public FidelityPointsServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _context = _fixture.CreateContext();
        _ruleServiceMock = new Mock<IPointEarningRuleService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _testUserId = Guid.NewGuid();
        _testAuditIdentifier = Guid.NewGuid().ToString();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(_testUserId);
        // Default-interface methods aren't invoked by Moq; stub explicitly.
        _currentUserServiceMock.Setup(x => x.GetAuditIdentifier()).Returns(_testAuditIdentifier);

        _service = new FidelityPointsService(
            _context,
            _currentUserServiceMock.Object,
            _ruleServiceMock.Object
        );

        await TestUserSeeder.SeedUserAsync(_context, _testUserId);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>
    /// Seeds a minimal Order and returns its id. Required because
    /// FidelityPointsTransaction.OrderId has a FK constraint to orders, even
    /// though the column is nullable on the entity.
    /// </summary>
    private async Task<Guid> SeedOrderAsync(Guid? userId = null)
    {
        var orderId = Guid.NewGuid();
        await TestOrderSeeder.SeedOrderAsync(_context, orderId, userId);
        return orderId;
    }

    private async Task<Guid> SeedAwardOrderAsync(
        Guid userId,
        int? candidatePoints,
        decimal rootTotal,
        int quantity = 1,
        bool includeSnapshot = true,
        bool evaluated = true,
        bool matchedZeroPointRule = false)
    {
        var orderId = Guid.NewGuid();
        await TestOrderSeeder.SeedOrderAsync(_context, orderId, userId);
        var order = await _context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Completed;
        order.SubTotal = rootTotal;
        order.Total = rootTotal;
        order.FidelityPointsEarned = candidatePoints ?? 0;
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Accepted award fixture",
            Quantity = quantity,
            UnitPrice = rootTotal / quantity,
            ItemTotal = rootTotal,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "AwardSnapshotTest"
        };
        order.Items.Add(item);
        _context.OrderItems.Add(item);
        await _context.SaveChangesAsync();

        if (!includeSnapshot)
            return orderId;

        var rule = candidatePoints is > 0 || matchedZeroPointRule
            ? new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Frozen test rule", 0m,
                null, candidatePoints ?? 0, 1)
            : null;
        var evaluation = evaluated
            ? new OrderBillingEarningEvaluation(
                candidatePoints, "fixed-priority-v1", new string('a', 64), rule)
            : new OrderBillingEarningEvaluation(null, null, null, null);
        var built = OrderBillingSnapshotFactory.Build(order, "CHF", evaluation, null,
            OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows);
        _context.OrderBillingSnapshots.Add(built.Header);
        _context.OrderBillingSnapshotUnits.AddRange(built.Units);
        _context.OrderBillingSnapshotOwnerLinks.AddRange(built.OwnerLinks);
        await _context.SaveChangesAsync();
        return orderId;
    }

    [Fact]
    public async Task CalculatePointsForOrderAsync_WithApplicableRule_ReturnsCorrectPoints()
    {
        // Arrange
        var orderTotal = 50m;
        var expectedPoints = 100;
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Test Rule",
            MinOrderAmount = 20m,
            MaxOrderAmount = 100m,
            PointsAwarded = expectedPoints,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _ruleServiceMock
            .Setup(x => x.FindApplicableRuleAsync(orderTotal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);

        // Act
        var points = await _service.CalculatePointsForOrderAsync(orderTotal);

        // Assert
        Assert.Equal(expectedPoints, points);
    }

    [Fact]
    public async Task CalculatePointsForOrderAsync_WithNoApplicableRule_ReturnsZero()
    {
        // Arrange
        var orderTotal = 5m;

        _ruleServiceMock
            .Setup(x => x.FindApplicableRuleAsync(orderTotal, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointEarningRule?)null);

        // Act
        var points = await _service.CalculatePointsForOrderAsync(orderTotal);

        // Assert
        Assert.Equal(0, points);
    }

    [Fact]
    public async Task AwardPointsAsync_CreatesTransactionAndUpdatesBalance()
    {
        // Arrange
        var userId = _testUserId;
        var points = 100;
        var orderTotal = 50m;
        var orderId = await SeedAwardOrderAsync(userId, points, orderTotal);

        // Act
        var result = await _service.AwardAcceptedOrderAsync(orderId);
        var transaction = await _context.FidelityPointsTransactions.SingleAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Earned);

        // Assert
        Assert.NotNull(transaction);
        Assert.Equal(FidelityPointsAwardDisposition.Awarded, result.Disposition);
        Assert.Equal(userId, transaction.UserId);
        Assert.Equal(orderId, transaction.OrderId);
        Assert.Equal(points, transaction.Points);
        Assert.Equal(TransactionType.Earned, transaction.TransactionType);
        Assert.Equal(_testAuditIdentifier, transaction.CreatedBy);

        // Verify balance was created/updated
        var balance = await _context.FidelityPointBalances
            .FirstOrDefaultAsync(b => b.UserId == userId);

        Assert.NotNull(balance);
        Assert.Equal(points, balance.CurrentPoints);
        Assert.Equal(points, balance.TotalEarnedPoints);
        Assert.Equal(0, balance.TotalRedeemedPoints);
        Assert.Equal(_testAuditIdentifier, balance.CreatedBy);
    }

    [Fact]
    public async Task AwardPointsAsync_MultipleAwards_AccumulatesBalance()
    {
        // Arrange
        var userId = _testUserId;
        var points1 = 100;
        var points2 = 50;
        var orderId1 = await SeedAwardOrderAsync(userId, points1, 50m);
        var orderId2 = await SeedAwardOrderAsync(userId, points2, 25m);

        // Act
        await _service.AwardAcceptedOrderAsync(orderId1);
        await _service.AwardAcceptedOrderAsync(orderId2);

        // Assert
        var balance = await _context.FidelityPointBalances
            .FirstOrDefaultAsync(b => b.UserId == userId);

        Assert.NotNull(balance);
        Assert.Equal(points1 + points2, balance.CurrentPoints);
        Assert.Equal(points1 + points2, balance.TotalEarnedPoints);
    }

    [Fact]
    public async Task RedeemPointsAsync_WithSufficientPoints_Success()
    {
        // Arrange
        var userId = _testUserId;
        var orderId = await SeedOrderAsync(userId);
        var availablePoints = 200;
        var pointsToRedeem = 100;

        // First award points
        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, availablePoints, 100m));

        // Act
        var result = await _service.RedeemPointsAsync(userId, orderId, pointsToRedeem);

        // Assert
        Assert.NotNull(result.Transaction);
        Assert.Equal(-pointsToRedeem, result.Transaction.Points);
        Assert.Equal(TransactionType.Redeemed, result.Transaction.TransactionType);
        Assert.Equal(1m, result.DiscountAmount); // 100 points = $1
        Assert.Equal(_testAuditIdentifier, result.Transaction.CreatedBy);

        // Verify balance
        var balance = await _context.FidelityPointBalances
            .FirstOrDefaultAsync(b => b.UserId == userId);

        Assert.NotNull(balance);
        Assert.Equal(availablePoints - pointsToRedeem, balance.CurrentPoints);
        Assert.Equal(pointsToRedeem, balance.TotalRedeemedPoints);
        Assert.Equal(_testAuditIdentifier, balance.UpdatedBy);
    }

    [Fact]
    public async Task RedeemPointsAsync_WithInsufficientPoints_ThrowsException()
    {
        // Arrange
        var userId = _testUserId;
        var orderId = await SeedOrderAsync(userId);
        var availablePoints = 50;
        var pointsToRedeem = 100;

        // First award points
        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, availablePoints, 25m));

        // The dedicated pre-debit refusal is the only failure native guest acceptance may suppress.
        await Assert.ThrowsAsync<RestaurantSystem.Api.Common.Exceptions.InsufficientPointsException>(
            () => _service.RedeemPointsAsync(userId, orderId, pointsToRedeem)
        );
        Assert.Equal(availablePoints, (await _service.GetUserBalanceAsync(userId))!.CurrentPoints);
        Assert.False(await _context.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == orderId && value.TransactionType == TransactionType.Redeemed));
    }

    [Fact]
    public async Task GetUserBalanceAsync_ReturnsCorrectBalance()
    {
        // Arrange
        var userId = _testUserId;
        var points = 150;

        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, points, 75m));

        // Act
        var balance = await _service.GetUserBalanceAsync(userId);

        // Assert
        Assert.NotNull(balance);
        Assert.Equal(userId, balance.UserId);
        Assert.Equal(points, balance.CurrentPoints);
    }

    [Fact]
    public async Task GetPointsHistoryAsync_ReturnsTransactionsOrderedByDate()
    {
        // Arrange
        var userId = _testUserId;

        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, 100, 50m));
        await Task.Delay(100); // Ensure different timestamps
        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, 50, 25m));
        await Task.Delay(100);
        await _service.RedeemPointsAsync(userId, await SeedOrderAsync(userId), 30);

        // Act
        var history = await _service.GetPointsHistoryAsync(userId);

        // Assert
        Assert.Equal(3, history.Count);
        Assert.True(history[0].CreatedAt >= history[1].CreatedAt);
        Assert.True(history[1].CreatedAt >= history[2].CreatedAt);
    }

    [Fact]
    public async Task AdjustPointsAsync_CreatesAdjustmentTransaction()
    {
        // Arrange
        var userId = _testUserId;
        var adjustmentPoints = 50;
        var reason = "Promotional bonus";

        // Act
        var transaction = await _service.AdjustPointsAsync(userId, adjustmentPoints, reason);

        // Assert
        Assert.NotNull(transaction);
        Assert.Equal(userId, transaction.UserId);
        Assert.Equal(adjustmentPoints, transaction.Points);
        Assert.Equal(TransactionType.AdminAdjustment, transaction.TransactionType);
        Assert.Equal(reason, transaction.Description);
    }

    [Fact]
    public async Task AdjustPointsAsync_NegativeAdjustment_CannotGoBelowZero()
    {
        // Arrange
        var userId = _testUserId;
        var initialPoints = 30;
        var negativeAdjustment = -50;

        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(userId, initialPoints, 15m));

        // Act
        await _service.AdjustPointsAsync(userId, negativeAdjustment, "Correction");

        // Assert
        var balance = await _context.FidelityPointBalances
            .FirstOrDefaultAsync(b => b.UserId == userId);

        Assert.NotNull(balance);
        Assert.Equal(0, balance.CurrentPoints); // Should not go below 0
    }

    [Theory]
    [InlineData(100, 1.0)]
    [InlineData(200, 2.0)]
    [InlineData(50, 0.5)]
    [InlineData(150, 1.5)]
    public void CalculateDiscountFromPoints_ReturnsCorrectAmount(int points, decimal expectedDiscount)
    {
        // Act
        var discount = _service.CalculateDiscountFromPoints(points);

        // Assert
        Assert.Equal(expectedDiscount, discount);
    }

    [Theory]
    [InlineData(1.0, 100)]
    [InlineData(2.5, 250)]
    [InlineData(0.5, 50)]
    [InlineData(1.99, 199)]
    public void CalculatePointsForDiscount_ReturnsCorrectPoints(decimal discountAmount, int expectedPoints)
    {
        // Act
        var points = _service.CalculatePointsForDiscount(discountAmount);

        // Assert
        Assert.Equal(expectedPoints, points);
    }

    [Fact]
    public async Task GetSystemAnalyticsAsync_ReturnsCorrectStatistics()
    {
        // Arrange — seed two distinct users (FK constraints on orders + transactions)
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(_context, user1);
        await TestUserSeeder.SeedUserAsync(_context, user2);

        // User 1: Earn 200, redeem 50
        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(user1, 200, 100m));
        await _service.RedeemPointsAsync(user1, await SeedOrderAsync(user1), 50);

        // User 2: Earn 300
        await _service.AwardAcceptedOrderAsync(await SeedAwardOrderAsync(user2, 300, 150m));

        // Act
        var analytics = await _service.GetSystemAnalyticsAsync();

        // Assert
        Assert.Equal(500, analytics.TotalPointsIssued); // 200 + 300
        Assert.Equal(50, analytics.TotalPointsRedeemed);
        Assert.Equal(2, analytics.TotalActiveUsers);
        Assert.Equal(450, analytics.TotalPointsOutstanding); // 150 + 300
        Assert.Equal(225m, analytics.AveragePointsPerUser); // 450 / 2
        Assert.Equal(0.5m, analytics.TotalDiscountGiven); // 50 points = $0.50
    }
}
