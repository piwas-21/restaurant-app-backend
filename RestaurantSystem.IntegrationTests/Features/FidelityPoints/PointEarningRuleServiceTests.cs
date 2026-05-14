using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Entities;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

[Collection("Database")]
public class PointEarningRuleServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private PointEarningRuleService _service = null!;
    private Mock<ICurrentUserService> _currentUserServiceMock = null!;

    public PointEarningRuleServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _context = _fixture.CreateContext();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        var testUserId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(testUserId);
        // Default-interface methods aren't invoked by Moq; stub explicitly.
        _currentUserServiceMock.Setup(x => x.GetAuditIdentifier()).Returns(testUserId.ToString());

        _service = new PointEarningRuleService(_context, _currentUserServiceMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetActiveRulesAsync_ReturnsOnlyActiveRules()
    {
        // Arrange
        var activeRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Active Rule",
            MinOrderAmount = 0m,
            MaxOrderAmount = 50m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var inactiveRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Inactive Rule",
            MinOrderAmount = 50m,
            MaxOrderAmount = 100m,
            PointsAwarded = 100,
            IsActive = false,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.AddRange(activeRule, inactiveRule);
        await _context.SaveChangesAsync();

        // Act
        var rules = await _service.GetActiveRulesAsync();

        // Assert
        Assert.Single(rules);
        Assert.Equal("Active Rule", rules[0].Name);
    }

    [Fact]
    public async Task GetActiveRulesAsync_OrdersByPriority()
    {
        // Arrange
        var rule1 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Low Priority",
            MinOrderAmount = 0m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var rule2 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "High Priority",
            MinOrderAmount = 0m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var rule3 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Medium Priority",
            MinOrderAmount = 0m,
            PointsAwarded = 75,
            IsActive = true,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.AddRange(rule1, rule2, rule3);
        await _context.SaveChangesAsync();

        // Act
        var rules = await _service.GetActiveRulesAsync();

        // Assert
        Assert.Equal(3, rules.Count);
        Assert.Equal("High Priority", rules[0].Name);
        Assert.Equal("Medium Priority", rules[1].Name);
        Assert.Equal("Low Priority", rules[2].Name);
    }

    [Fact]
    public async Task FindApplicableRuleAsync_ReturnsMatchingRule()
    {
        // Arrange
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Mid Range Rule",
            MinOrderAmount = 20m,
            MaxOrderAmount = 100m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(rule);
        await _context.SaveChangesAsync();

        // Act
        var foundRule = await _service.FindApplicableRuleAsync(50m);

        // Assert
        Assert.NotNull(foundRule);
        Assert.Equal("Mid Range Rule", foundRule.Name);
    }

    [Fact]
    public async Task FindApplicableRuleAsync_WithNoMaxAmount_MatchesHighOrders()
    {
        // Arrange
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Unlimited Max",
            MinOrderAmount = 100m,
            MaxOrderAmount = null,
            PointsAwarded = 500,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(rule);
        await _context.SaveChangesAsync();

        // Act
        var foundRule = await _service.FindApplicableRuleAsync(1000m);

        // Assert
        Assert.NotNull(foundRule);
        Assert.Equal("Unlimited Max", foundRule.Name);
    }

    [Fact]
    public async Task FindApplicableRuleAsync_WithMultipleMatches_ReturnsHighestPriority()
    {
        // Arrange
        var rule1 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Lower Priority",
            MinOrderAmount = 0m,
            MaxOrderAmount = 100m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var rule2 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Higher Priority",
            MinOrderAmount = 0m,
            MaxOrderAmount = 100m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.AddRange(rule1, rule2);
        await _context.SaveChangesAsync();

        // Act
        var foundRule = await _service.FindApplicableRuleAsync(50m);

        // Assert
        Assert.NotNull(foundRule);
        Assert.Equal("Higher Priority", foundRule.Name);
    }

    [Fact]
    public async Task CreateRuleAsync_CreatesRule()
    {
        // Arrange
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "New Rule",
            MinOrderAmount = 50m,
            MaxOrderAmount = 150m,
            PointsAwarded = 200,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var createdRule = await _service.CreateRuleAsync(rule);

        // Assert
        Assert.NotNull(createdRule);
        var dbRule = await _context.PointEarningRules.FindAsync(rule.Id);
        Assert.NotNull(dbRule);
        Assert.Equal("New Rule", dbRule.Name);
    }

    [Fact]
    public async Task UpdateRuleAsync_UpdatesRule()
    {
        // Arrange
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
            MinOrderAmount = 0m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(rule);
        await _context.SaveChangesAsync();

        // Detach to simulate new context
        _context.Entry(rule).State = EntityState.Detached;

        rule.Name = "Updated Name";
        rule.PointsAwarded = 100;

        // Act
        var updatedRule = await _service.UpdateRuleAsync(rule);

        // Assert
        Assert.Equal("Updated Name", updatedRule.Name);
        Assert.Equal(100, updatedRule.PointsAwarded);
        Assert.NotNull(updatedRule.UpdatedAt);
    }

    [Fact]
    public async Task DeleteRuleAsync_DeletesRule()
    {
        // Arrange
        var rule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "To Delete",
            MinOrderAmount = 0m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(rule);
        await _context.SaveChangesAsync();

        // Act
        await _service.DeleteRuleAsync(rule.Id);

        // Assert
        var deletedRule = await _context.PointEarningRules.FindAsync(rule.Id);
        Assert.Null(deletedRule);
    }

    [Fact]
    public async Task ValidateNoOverlapAsync_WithNoOverlap_ReturnsTrue()
    {
        // Arrange
        var existingRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            MinOrderAmount = 0m,
            MaxOrderAmount = 50m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(existingRule);
        await _context.SaveChangesAsync();

        var newRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "New",
            MinOrderAmount = 51m,
            MaxOrderAmount = 100m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = await _service.ValidateNoOverlapAsync(newRule);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateNoOverlapAsync_WithOverlap_ReturnsFalse()
    {
        // Arrange
        var existingRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            MinOrderAmount = 20m,
            MaxOrderAmount = 100m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.Add(existingRule);
        await _context.SaveChangesAsync();

        var overlappingRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Overlapping",
            MinOrderAmount = 50m,
            MaxOrderAmount = 150m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = await _service.ValidateNoOverlapAsync(overlappingRule);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task GetActiveRulesCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var activeRule1 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Active 1",
            MinOrderAmount = 0m,
            PointsAwarded = 50,
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var activeRule2 = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Active 2",
            MinOrderAmount = 50m,
            PointsAwarded = 100,
            IsActive = true,
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var inactiveRule = new PointEarningRule
        {
            Id = Guid.NewGuid(),
            Name = "Inactive",
            MinOrderAmount = 100m,
            PointsAwarded = 150,
            IsActive = false,
            Priority = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.PointEarningRules.AddRange(activeRule1, activeRule2, inactiveRule);
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.GetActiveRulesCountAsync();

        // Assert
        Assert.Equal(2, count);
    }
}
