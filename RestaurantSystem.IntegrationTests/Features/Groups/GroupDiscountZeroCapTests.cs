using Microsoft.Extensions.Configuration;
using Moq;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.Groups.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Groups;

/// <summary>
/// <c>GroupDiscount.MaximumDiscountAmount</c> is a <c>decimal?</c> where NULL means
/// "no cap". TWO services read that same column on the same rows, and before this
/// suite they disagreed about what a stored <c>0</c> means:
///
/// <list type="bullet">
///   <item><description>
///     <see cref="CustomerDiscountService"/> (basket path) has always treated
///     <c>0</c> as "no cap", and says so in a comment at the guard itself —
///     "handle potential data entry errors where 0 was used instead of null".
///   </description></item>
///   <item><description>
///     <see cref="MembershipQrService"/> (membership-QR path) capped on
///     <c>HasValue</c> alone, so a stored <c>0</c> clamped every discount to zero:
///     the discount silently stopped discounting.
///   </description></item>
/// </list>
///
/// The disagreement is the defect — not the individual rule. Which service the
/// guest happens to be routed through must not change the money. A <c>0</c> is
/// reachable in production: the admin discount form coerces the API's <c>null</c>
/// (and an emptied input) to <c>0</c> on every save, so an untouched "uncapped"
/// discount is rewritten to a cap of zero.
///
/// The parity test below is the load-bearing one: its expected value is not a
/// number computed here, it is whatever the already-guarded sibling returns.
/// </summary>
[Collection("Database Lane 4")]
public class GroupDiscountZeroCapTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private MembershipQrService _qrPath = null!;
    private CustomerDiscountService _basketPath = null!;
    private Guid _testUserId;

    public GroupDiscountZeroCapTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        _context = _fixture.CreateContext();

        _testUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.UserId).Returns(_testUserId);
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(_testUserId.ToString());

        _qrPath = new MembershipQrService(_context, new QRCodeService(new ConfigurationBuilder().Build()));
        _basketPath = new CustomerDiscountService(_context, currentUser.Object);

        await TestUserSeeder.SeedUserAsync(_context, _testUserId);
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    // ----------------------------------------------------------------- helpers

    private async Task<GroupMembership> SeedAsync(DiscountType type, decimal value, decimal? max)
    {
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Name = "Zero Cap Group",
            Description = "A group",
            QRCodeData = "ZEROCAPGROUP123",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        };

        group.Discounts.Add(new GroupDiscount
        {
            Id = Guid.NewGuid(),
            Name = "Discount",
            Type = type,
            Value = value,
            MinimumOrderAmount = null,
            MaximumDiscountAmount = max,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        });

        var membership = new GroupMembership
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = _testUserId,
            UniqueQRCode = "UNUSED",
            IsActive = true,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        };

        _context.UserGroups.Add(group);
        _context.GroupMemberships.Add(membership);
        await _context.SaveChangesAsync();
        return membership;
    }

    // --------------------------------------------------- the defect: 0 = no cap

    [Fact]
    public async Task ZeroMaximumCap_DoesNotSuppressAPercentageDiscount()
    {
        // 10% of 100 = 10. A stored cap of 0 must not clamp it to nothing.
        var membership = await SeedAsync(DiscountType.Percentage, 10m, max: 0m);

        var amount = await _qrPath.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(10m, amount);
    }

    [Fact]
    public async Task ZeroMaximumCap_DoesNotSuppressAFixedAmountDiscount()
    {
        var membership = await SeedAsync(DiscountType.FixedAmount, 7m, max: 0m);

        var amount = await _qrPath.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(7m, amount);
    }

    /// <summary>
    /// The oracle this suite exists for: the expected value is the answer the
    /// ALREADY-GUARDED sibling gives for the very same row, not a number computed
    /// here. If the two paths ever diverge again on this column, this fails
    /// without anyone having to remember what the right amount was.
    /// </summary>
    [Fact]
    public async Task ZeroMaximumCap_QrPathAgreesWithTheBasketPath()
    {
        var membership = await SeedAsync(DiscountType.Percentage, 10m, max: 0m);

        var basketRule = await _basketPath.FindBestApplicableDiscountAsync(_testUserId, 100m);
        var qrAmount = await _qrPath.CalculateDiscountAsync(membership.Id, 100m);

        Assert.NotNull(basketRule);
        Assert.Equal(basketRule!.DiscountValue, qrAmount);
    }

    // ------------------------------------------- controls: the cap still caps
    //
    // The fix LOOSENS a condition, and loosening is silent. These two say so out
    // loud: a real cap must still bite, and NULL must still mean "no cap".

    [Fact]
    public async Task PositiveMaximumCap_StillCaps()
    {
        // 50% of 100 = 50, capped at 10.
        var membership = await SeedAsync(DiscountType.Percentage, 50m, max: 10m);

        var amount = await _qrPath.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(10m, amount);
    }

    [Fact]
    public async Task NullMaximumCap_DoesNotCap()
    {
        var membership = await SeedAsync(DiscountType.Percentage, 50m, max: null);

        var amount = await _qrPath.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(50m, amount);
    }
}
