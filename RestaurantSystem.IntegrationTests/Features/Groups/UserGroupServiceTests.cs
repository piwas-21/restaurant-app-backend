using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RestaurantSystem.IntegrationTests.Features.Groups;

/// <summary>
/// Characterization tests for the customer loyalty-group feature (QR-based
/// membership → discount). These pin the CURRENT observable behavior of the
/// <see cref="IUserGroupService"/> surface — group CRUD, membership lifecycle,
/// the QR-validation state machine, and the money path
/// (<c>CalculateDiscountAsync</c>) — as a regression net for the facade
/// refactor that splits <see cref="UserGroupService"/> into collaborators.
///
/// They exercise the service through its public interface with a real
/// <see cref="QRCodeService"/> (so HMAC signing/validation runs end-to-end) and
/// a real Postgres (Testcontainers). Only <see cref="ICurrentUserService"/> and
/// <see cref="IEmailService"/> are mocked. Every assertion here holds identically
/// before and after the extraction — the split is behavior-preserving.
/// </summary>
[Collection("Database Lane 1")]
public class UserGroupServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private UserGroupService _service = null!;
    private QRCodeService _qrCodeService = null!;
    private Mock<ICurrentUserService> _currentUserServiceMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Guid _testUserId;

    public UserGroupServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _context = _fixture.CreateContext();

        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _testUserId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(_testUserId);
        _currentUserServiceMock.Setup(x => x.GetAuditIdentifier()).Returns(_testUserId.ToString());

        // Real QR service so signature generation/validation is exercised for
        // real (empty config → the service's built-in default secret key).
        _qrCodeService = new QRCodeService(new ConfigurationBuilder().Build());

        _emailServiceMock = new Mock<IEmailService>();

        // Build the facade over its real collaborators (all sharing this test's
        // DbContext + QR service). The refactor is behavior-preserving, so every
        // assertion below holds against the facade exactly as it did against the
        // pre-split monolith.
        var membershipService = new GroupMembershipService(
            _context,
            _qrCodeService,
            _currentUserServiceMock.Object,
            _emailServiceMock.Object,
            TestEmailLanguages.Resolver(),
            NullLogger<GroupMembershipService>.Instance);
        var membershipQrService = new MembershipQrService(_context, _qrCodeService);

        _service = new UserGroupService(
            _context,
            _qrCodeService,
            _currentUserServiceMock.Object,
            membershipService,
            membershipQrService);

        await TestUserSeeder.SeedUserAsync(_context, _testUserId);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    // ----------------------------------------------------------------- helpers

    private async Task<UserGroup> SeedGroupAsync(
        bool isActive = true,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        params GroupDiscount[] discounts)
    {
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            Description = "A group",
            QRCodeData = "GROUPCODE123456",
            IsActive = isActive,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        };

        foreach (var d in discounts)
        {
            group.Discounts.Add(d);
        }

        _context.UserGroups.Add(group);
        await _context.SaveChangesAsync();
        return group;
    }

    private static GroupDiscount Discount(
        DiscountType type,
        decimal value,
        decimal? min = null,
        decimal? max = null,
        bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Discount",
            Type = type,
            Value = value,
            MinimumOrderAmount = min,
            MaximumDiscountAmount = max,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        };

    private async Task<GroupMembership> SeedMembershipAsync(
        Guid groupId,
        bool isActive = true,
        DateTime? expiresAt = null)
    {
        var membership = new GroupMembership
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            UserId = _testUserId,
            UniqueQRCode = "UNUSED",
            IsActive = isActive,
            JoinedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSeed"
        };

        _context.GroupMemberships.Add(membership);
        await _context.SaveChangesAsync();
        return membership;
    }

    /// <summary>Builds a correctly-signed QR string for arbitrary ids (matches the format AddMemberAsync emits).</summary>
    private string BuildSignedQr(Guid groupId, Guid userId, Guid membershipId)
    {
        var data = $"GROUP:{groupId}:USER:{userId}:MEMBERSHIP:{membershipId}";
        var signature = _qrCodeService.GenerateSignature(data);
        return $"{data}:SIG:{signature}";
    }

    // -------------------------------------------------------------- group CRUD

    [Fact]
    public async Task CreateGroupAsync_PersistsGroup_WithGeneratedQrAndDefaults()
    {
        var dto = new CreateUserGroupDto
        {
            Name = "VIP",
            Description = "VIP members"
        };

        var result = await _service.CreateGroupAsync(dto);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("VIP", result.Name);
        Assert.Equal("VIP members", result.Description);
        Assert.True(result.IsActive);
        Assert.Equal(0, result.MemberCount);
        Assert.Empty(result.Discounts);
        // GenerateUniqueCode → 16 upper-hex chars.
        Assert.Equal(16, result.QRCodeData.Length);
        Assert.Equal(result.QRCodeData.ToUpperInvariant(), result.QRCodeData);

        var persisted = await _context.UserGroups.FindAsync(result.Id);
        Assert.NotNull(persisted);
        Assert.Equal(_testUserId.ToString(), persisted!.CreatedBy);
    }

    [Fact]
    public async Task CreateGroupAsync_WithInitialDiscount_MapsDiscountFields()
    {
        var dto = new CreateUserGroupDto
        {
            Name = "Loyalty",
            Description = "Loyal customers",
            InitialDiscount = new CreateGroupDiscountDto
            {
                Name = "10 percent",
                Type = DiscountType.Percentage,
                Value = 10m,
                MinimumOrderAmount = 20m,
                MaximumDiscountAmount = 50m
            }
        };

        var result = await _service.CreateGroupAsync(dto);

        var discount = Assert.Single(result.Discounts);
        Assert.Equal("10 percent", discount.Name);
        Assert.Equal(DiscountType.Percentage, discount.Type);
        Assert.Equal(10m, discount.Value);
        Assert.Equal(20m, discount.MinimumOrderAmount);
        Assert.Equal(50m, discount.MaximumDiscountAmount);
        Assert.True(discount.IsActive);
        Assert.Equal(result.Id, discount.GroupId);
    }

    [Fact]
    public async Task GetGroupByIdAsync_ReturnsMappedGroup_WithMemberCountAndDiscounts()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 15m));
        await SeedMembershipAsync(group.Id);

        var result = await _service.GetGroupByIdAsync(group.Id);

        Assert.NotNull(result);
        Assert.Equal(group.Id, result!.Id);
        Assert.Equal("Test Group", result.Name);
        Assert.Equal("GROUPCODE123456", result.QRCodeData);
        Assert.Equal(1, result.MemberCount);
        var discount = Assert.Single(result.Discounts);
        Assert.Equal(15m, discount.Value);
    }

    [Fact]
    public async Task GetGroupByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _service.GetGroupByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllGroupsAsync_ReturnsAllGroups()
    {
        await SeedGroupAsync();
        await SeedGroupAsync();

        var result = await _service.GetAllGroupsAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task UpdateGroupAsync_UpdatesEditableFields()
    {
        var group = await SeedGroupAsync(isActive: true);

        var updated = await _service.UpdateGroupAsync(new UpdateUserGroupDto
        {
            Id = group.Id,
            Name = "Renamed",
            Description = "New description",
            IsActive = false
        });

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("New description", updated.Description);
        Assert.False(updated.IsActive);

        var persisted = await _context.UserGroups.FindAsync(group.Id);
        Assert.Equal("Renamed", persisted!.Name);
        Assert.NotNull(persisted.UpdatedAt);
    }

    [Fact]
    public async Task UpdateGroupAsync_NotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateGroupAsync(new UpdateUserGroupDto
            {
                Id = Guid.NewGuid(),
                Name = "x",
                Description = "y",
                IsActive = true
            }));
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesGroup()
    {
        var group = await SeedGroupAsync();

        await _service.DeleteGroupAsync(group.Id);

        Assert.Null(await _service.GetGroupByIdAsync(group.Id));
    }

    [Fact]
    public async Task DeleteGroupAsync_NotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.DeleteGroupAsync(Guid.NewGuid()));
    }

    // ------------------------------------------------------------- memberships

    [Fact]
    public async Task AddMemberAsync_CreatesMembership_AndSendsConfirmationEmail()
    {
        var group = await SeedGroupAsync();

        var result = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        Assert.Equal(group.Id, result.GroupId);
        Assert.Equal(_testUserId, result.UserId);
        Assert.True(result.IsActive);
        Assert.StartsWith($"GROUP:{group.Id}:USER:{_testUserId}:MEMBERSHIP:", result.UniqueQRCode);
        Assert.Contains(":SIG:", result.UniqueQRCode);

        var persisted = await _context.GroupMemberships.FindAsync(result.Id);
        Assert.NotNull(persisted);

        _emailServiceMock.Verify(
            x => x.SendMembershipConfirmationEmailAsync(
                It.IsAny<CultureInfo>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Issue #187. The confirmation-email failure path used to `Console.WriteLine` the
    /// recipient's address, putting a plaintext PII trail in the container logs (DEV-PHASES
    /// D7, docs/privacy PII map). This pins both halves of the fix: the address must not be
    /// in the log, and the correlating IDs must be — a log that leaked nothing AND said
    /// nothing would pass a naive "no email in output" check while making a failed send
    /// impossible to chase.
    /// </summary>
    [Fact]
    public async Task AddMemberAsync_EmailSendFails_LogsIdsAndNeverTheAddress()
    {
        var group = await SeedGroupAsync();
        var user = await _context.Users.FirstAsync(u => u.Id == _testUserId);
        var logger = new Mock<ILogger<GroupMembershipService>>();

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(x => x.SendMembershipConfirmationEmailAsync(
                It.IsAny<CultureInfo>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp exploded"));

        var service = new GroupMembershipService(
            _context,
            _qrCodeService,
            _currentUserServiceMock.Object,
            emailServiceMock.Object,
            TestEmailLanguages.Resolver(),
            logger.Object);

        // Unchanged behaviour: a failed email must never fail membership creation.
        var result = await service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });
        Assert.NotNull(await _context.GroupMemberships.FindAsync(result.Id));

        var logged = new List<string>();
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => Capture(v, logged)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        var message = Assert.Single(logged);
        Assert.DoesNotContain(user.Email!, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_testUserId.ToString(), message);
        Assert.Contains(result.Id.ToString(), message);
    }

    private static bool Capture(object state, List<string> sink)
    {
        sink.Add(state.ToString() ?? string.Empty);
        return true;
    }

    [Fact]
    public async Task AddMemberAsync_DuplicateMember_ThrowsBadRequest()
    {
        var group = await SeedGroupAsync();
        await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId }));

        Assert.Equal("User is already a member of this group", ex.Message);
    }

    [Fact]
    public async Task AddMemberAsync_GroupNotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AddMemberAsync(Guid.NewGuid(), new AddMemberDto { UserId = _testUserId }));
    }

    [Fact]
    public async Task AddMemberAsync_UserNotFound_ThrowsKeyNotFound()
    {
        var group = await SeedGroupAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task AddMemberAsync_EmailFailure_IsSwallowed_MembershipStillCreated()
    {
        var group = await SeedGroupAsync();
        _emailServiceMock
            .Setup(x => x.SendMembershipConfirmationEmailAsync(
                It.IsAny<CultureInfo>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        // The email failure is caught and logged; membership creation succeeds.
        var result = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.NotNull(await _context.GroupMemberships.FindAsync(result.Id));
    }

    [Fact]
    public async Task RemoveMemberAsync_RemovesMembership()
    {
        var group = await SeedGroupAsync();
        await SeedMembershipAsync(group.Id);

        await _service.RemoveMemberAsync(group.Id, _testUserId);

        Assert.Empty(await _service.GetGroupMembersAsync(group.Id));
    }

    [Fact]
    public async Task RemoveMemberAsync_NotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RemoveMemberAsync(Guid.NewGuid(), _testUserId));
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsMembersWithUserDetails()
    {
        var group = await SeedGroupAsync();
        await SeedMembershipAsync(group.Id);

        var members = await _service.GetGroupMembersAsync(group.Id);

        var member = Assert.Single(members);
        Assert.Equal(_testUserId, member.UserId);
        Assert.False(string.IsNullOrEmpty(member.UserEmail));
    }

    [Fact]
    public async Task GetMemberQRCodeImageAsync_ReturnsPngBytes()
    {
        var group = await SeedGroupAsync();
        var membership = await SeedMembershipAsync(group.Id);

        var image = await _service.GetMemberQRCodeImageAsync(membership.Id);

        Assert.NotNull(image);
        Assert.NotEmpty(image);
    }

    [Fact]
    public async Task GetMemberQRCodeImageAsync_NotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.GetMemberQRCodeImageAsync(Guid.NewGuid()));
    }

    // ----------------------------------------------- ValidateMembershipByQRCode

    [Fact]
    public async Task ValidateQr_ValidMembership_ReturnsValidResultWithGroupAndDiscounts()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 10m));
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.True(result.IsValid);
        Assert.Equal("Valid membership", result.Message);
        Assert.NotNull(result.Membership);
        Assert.Equal(added.Id, result.Membership!.Id);
        Assert.NotNull(result.Group);
        // The validation projection hardcodes MemberCount = 0.
        Assert.Equal(0, result.Group!.MemberCount);
        Assert.Single(result.ApplicableDiscounts);
    }

    [Fact]
    public async Task ValidateQr_OnlyActiveDiscountsAreApplicable()
    {
        var group = await SeedGroupAsync(
            discounts: new[]
            {
                Discount(DiscountType.Percentage, 10m, isActive: true),
                Discount(DiscountType.Percentage, 20m, isActive: false)
            });
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.True(result.IsValid);
        var applicable = Assert.Single(result.ApplicableDiscounts);
        Assert.Equal(10m, applicable.Value);
    }

    [Fact]
    public async Task ValidateQr_InvalidFormat_ReturnsInvalid()
    {
        var result = await _service.ValidateMembershipByQRCodeAsync("not-a-valid-qr");

        Assert.False(result.IsValid);
        Assert.Equal("Invalid QR code format", result.Message);
    }

    [Fact]
    public async Task ValidateQr_InvalidSignature_ReturnsInvalid()
    {
        var group = await SeedGroupAsync();
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });
        // Replace the signature segment with a bogus value.
        var tampered = added.UniqueQRCode[..(added.UniqueQRCode.IndexOf(":SIG:", StringComparison.Ordinal) + 5)] + "tampered";

        var result = await _service.ValidateMembershipByQRCodeAsync(tampered);

        Assert.False(result.IsValid);
        Assert.Equal("Invalid QR code signature", result.Message);
    }

    [Fact]
    public async Task ValidateQr_MembershipNotFound_ReturnsInvalid()
    {
        // Correctly signed QR for ids that were never persisted.
        var qr = BuildSignedQr(Guid.NewGuid(), _testUserId, Guid.NewGuid());

        var result = await _service.ValidateMembershipByQRCodeAsync(qr);

        Assert.False(result.IsValid);
        Assert.Equal("Membership not found", result.Message);
    }

    [Fact]
    public async Task ValidateQr_InactiveMembership_ReturnsInvalid()
    {
        var group = await SeedGroupAsync();
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });
        var membership = await _context.GroupMemberships.FindAsync(added.Id);
        membership!.IsActive = false;
        await _context.SaveChangesAsync();

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.False(result.IsValid);
        Assert.Equal("Membership is inactive", result.Message);
    }

    [Fact]
    public async Task ValidateQr_ExpiredMembership_ReturnsInvalid()
    {
        var group = await SeedGroupAsync();
        var added = await _service.AddMemberAsync(
            group.Id,
            new AddMemberDto { UserId = _testUserId, ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.False(result.IsValid);
        Assert.Equal("Membership has expired", result.Message);
    }

    [Fact]
    public async Task ValidateQr_InactiveGroup_ReturnsInvalid()
    {
        var group = await SeedGroupAsync(isActive: false);
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.False(result.IsValid);
        Assert.Equal("Group is inactive", result.Message);
    }

    [Fact]
    public async Task ValidateQr_GroupNotYetValid_ReturnsInvalid()
    {
        var group = await SeedGroupAsync(validFrom: DateTime.UtcNow.AddDays(1));
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.False(result.IsValid);
        Assert.Equal("Group is not yet valid", result.Message);
    }

    [Fact]
    public async Task ValidateQr_GroupValidityExpired_ReturnsInvalid()
    {
        var group = await SeedGroupAsync(validUntil: DateTime.UtcNow.AddDays(-1));
        var added = await _service.AddMemberAsync(group.Id, new AddMemberDto { UserId = _testUserId });

        var result = await _service.ValidateMembershipByQRCodeAsync(added.UniqueQRCode);

        Assert.False(result.IsValid);
        Assert.Equal("Group validity has expired", result.Message);
    }

    [Fact]
    public async Task ValidateQr_MalformedGuidPayload_ReturnsErrorMessage()
    {
        // Well-formed envelope (8 segments, correct markers) but the ids don't
        // parse → the catch-all branch reports the error message.
        var result = await _service.ValidateMembershipByQRCodeAsync(
            "GROUP:not-a-guid:USER:nope:MEMBERSHIP:bad:SIG:whatever");

        Assert.False(result.IsValid);
        Assert.StartsWith("Error validating QR code:", result.Message);
    }

    // -------------------------------------------------------- CalculateDiscount

    [Fact]
    public async Task CalculateDiscount_PercentageDiscount_ReturnsPercentageOfOrder()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 10m));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(10m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_FixedAmountDiscount_ReturnsFixedValue()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.FixedAmount, 7m));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(7m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_AppliesMaximumCap()
    {
        // 50% of 100 = 50, but capped at 10.
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 50m, max: 10m));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(10m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_OrderBelowMinimum_ReturnsZero()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 10m, min: 50m));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 30m);

        Assert.Equal(0m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_InactiveDiscount_ReturnsZero()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 10m, isActive: false));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(0m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_ChoosesHighestDiscount()
    {
        var group = await SeedGroupAsync(
            discounts: new[]
            {
                Discount(DiscountType.Percentage, 10m),
                Discount(DiscountType.Percentage, 20m)
            });
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 100m);

        Assert.Equal(20m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_ZeroOrderAmount_ReturnsZero()
    {
        var group = await SeedGroupAsync(discounts: Discount(DiscountType.Percentage, 10m));
        var membership = await SeedMembershipAsync(group.Id);

        var discount = await _service.CalculateDiscountAsync(membership.Id, 0m);

        Assert.Equal(0m, discount);
    }

    [Fact]
    public async Task CalculateDiscount_MembershipNotFound_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.CalculateDiscountAsync(Guid.NewGuid(), 100m));
    }
}
