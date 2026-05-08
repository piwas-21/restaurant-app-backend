using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Entities;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

[Collection("Database")]
public class CustomerDiscountServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private CustomerDiscountService _service = null!;
    private Mock<ICurrentUserService> _currentUserServiceMock = null!;
    private Guid _testUserId;

    public CustomerDiscountServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Reset DB so each test starts clean — the fixture is shared across
        // tests in [Collection("Database")] but DB state is not implicitly
        // isolated by xunit.
        await _fixture.ResetDatabaseAsync();

        _context = _fixture.CreateContext();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _testUserId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(_testUserId);
        // Default-interface methods aren't invoked by Moq; stub explicitly.
        _currentUserServiceMock.Setup(x => x.GetAuditIdentifier()).Returns(_testUserId.ToString());

        _service = new CustomerDiscountService(_context, _currentUserServiceMock.Object);

        await TestUserSeeder.SeedUserAsync(_context, _testUserId);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetActiveDiscountsForUserAsync_ReturnsOnlyActiveDiscounts()
    {
        // Arrange
        var userId = _testUserId;
        var now = DateTime.UtcNow;

        var activeDiscount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Active Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            ValidFrom = now.AddDays(-1),
            ValidUntil = now.AddDays(30),
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        var inactiveDiscount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Inactive Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 15m,
            IsActive = false,
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.AddRange(activeDiscount, inactiveDiscount);
        await _context.SaveChangesAsync();

        // Act
        var discounts = await _service.GetActiveDiscountsForUserAsync(userId);

        // Assert
        Assert.Single(discounts);
        Assert.Equal("Active Discount", discounts[0].Name);
    }

    [Fact]
    public async Task FindBestApplicableDiscountAsync_ReturnsHighestDiscount()
    {
        // Arrange
        var userId = _testUserId;
        var orderAmount = 100m;

        var discount1 = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "10% Off",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        var discount2 = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "20% Off",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.AddRange(discount1, discount2);
        await _context.SaveChangesAsync();

        // Act
        var bestDiscount = await _service.FindBestApplicableDiscountAsync(userId, orderAmount);

        // Assert
        Assert.NotNull(bestDiscount);
        Assert.Equal("20% Off", bestDiscount.Name);
    }

    [Theory]
    [InlineData(100, 10, 10)]     // 10% of 100 = 10
    [InlineData(50, 20, 10)]      // 20% of 50 = 10
    [InlineData(200, 15, 30)]     // 15% of 200 = 30
    public void CalculateDiscountAmount_Percentage_ReturnsCorrectAmount(
        decimal orderAmount,
        decimal discountValue,
        decimal expectedDiscount)
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Percentage Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = discountValue,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var amount = _service.CalculateDiscountAmount(discount, orderAmount);

        // Assert
        Assert.Equal(expectedDiscount, amount);
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(50, 5)]
    [InlineData(200, 5)]
    public void CalculateDiscountAmount_FixedAmount_ReturnsFixedValue(
        decimal orderAmount,
        decimal fixedDiscount)
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Fixed Discount",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = fixedDiscount,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var amount = _service.CalculateDiscountAmount(discount, orderAmount);

        // Assert
        Assert.Equal(fixedDiscount, amount);
    }

    [Fact]
    public async Task ApplyDiscountAsync_IncrementsUsageCount()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            UsageCount = 0,
            MaxUsageCount = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.Add(discount);
        await _context.SaveChangesAsync();

        // Act
        var appliedDiscount = await _service.ApplyDiscountAsync(discount.Id);

        // Assert
        Assert.Equal(1, appliedDiscount.UsageCount);
    }

    [Fact]
    public async Task CreateDiscountAsync_CreatesDiscount()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "New Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 15m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var created = await _service.CreateDiscountAsync(discount);

        // Assert
        Assert.NotNull(created);
        var dbDiscount = await _context.CustomerDiscountRules.FindAsync(discount.Id);
        Assert.NotNull(dbDiscount);
        Assert.Equal("New Discount", dbDiscount.Name);
    }

    [Fact]
    public async Task UpdateDiscountAsync_UpdatesDiscount()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Original",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.Add(discount);
        await _context.SaveChangesAsync();

        _context.Entry(discount).State = EntityState.Detached;

        discount.Name = "Updated";
        discount.DiscountValue = 20m;

        // Act
        var updated = await _service.UpdateDiscountAsync(discount);

        // Assert
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(20m, updated.DiscountValue);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task DeleteDiscountAsync_DeactivatesDiscount()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "To Delete",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.Add(discount);
        await _context.SaveChangesAsync();

        // Act
        await _service.DeleteDiscountAsync(discount.Id);

        // Assert
        var deletedDiscount = await _context.CustomerDiscountRules.FindAsync(discount.Id);
        Assert.NotNull(deletedDiscount);
        Assert.False(deletedDiscount.IsActive);
    }

    [Fact]
    public void IsDiscountValid_ActiveWithinDates_ReturnsTrue()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Valid Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            ValidFrom = now.AddDays(-1),
            ValidUntil = now.AddDays(1),
            MinOrderAmount = 20m,
            MaxOrderAmount = 200m,
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = _service.IsDiscountValid(discount, 50m);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void IsDiscountValid_Expired_ReturnsFalse()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Expired Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            ValidFrom = now.AddDays(-30),
            ValidUntil = now.AddDays(-1),
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = _service.IsDiscountValid(discount, 50m);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsDiscountValid_OrderBelowMinimum_ReturnsFalse()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Min Order Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            MinOrderAmount = 50m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = _service.IsDiscountValid(discount, 30m);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsDiscountValid_UsageLimitReached_ReturnsFalse()
    {
        // Arrange
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Limited Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            MaxUsageCount = 5,
            UsageCount = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestUser"
        };

        // Act
        var isValid = _service.IsDiscountValid(discount, 50m);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task GetActiveDiscountsCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var active1 = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Active 1",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            ValidFrom = now.AddDays(-1),
            ValidUntil = now.AddDays(30),
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        var active2 = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Active 2",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 5m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        var inactive = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Inactive",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 15m,
            IsActive = false,
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        var expired = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Expired",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            IsActive = true,
            ValidFrom = now.AddDays(-30),
            ValidUntil = now.AddDays(-1),
            CreatedAt = now,
            CreatedBy = "TestUser"
        };

        _context.CustomerDiscountRules.AddRange(active1, active2, inactive, expired);
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.GetActiveDiscountsCountAsync();

        // Assert
        Assert.Equal(2, count);
    }
}
