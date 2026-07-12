using FluentAssertions;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Mapping;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

// Pure characterization of CustomerDiscountRuleMapper — the single home for the
// CustomerDiscountRule -> CustomerDiscountRuleDto projection that the refactor
// extracted from the controller's four duplicated call sites. These lock in the
// exact field-for-field mapping and the two per-shape "Unknown" fallbacks,
// including the subtle divergence for a present-user-with-null-email (list shape
// yields "", single shape yields "Unknown") that must be preserved.
public class CustomerDiscountRuleMapperTests
{
    private static CustomerDiscountRule SampleRule(Guid? userId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId ?? Guid.NewGuid(),
        Name = "Sample",
        DiscountType = DiscountType.Percentage,
        DiscountValue = 12.5m,
        MinOrderAmount = 20m,
        MaxOrderAmount = 200m,
        MaxUsageCount = 5,
        UsageCount = 2,
        IsActive = true,
        ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ValidUntil = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "seed",
    };

    // ------------------------------------------------------------ list shape

    [Fact]
    public void ToDtoFromLookup_UserPresent_MapsEveryFieldAndEnrichment()
    {
        var rule = SampleRule();
        var lookup = new Dictionary<Guid, (string? Email, string FirstName, string LastName)>
        {
            [rule.UserId] = ("ada@example.com", "Ada", "Lovelace"),
        };

        var dto = CustomerDiscountRuleMapper.ToDtoFromLookup(rule, lookup);

        dto.Id.Should().Be(rule.Id);
        dto.UserId.Should().Be(rule.UserId);
        dto.UserEmail.Should().Be("ada@example.com");
        dto.UserName.Should().Be("Ada Lovelace");
        dto.Name.Should().Be("Sample");
        dto.DiscountType.Should().Be("Percentage");
        dto.DiscountValue.Should().Be(12.5m);
        dto.MinOrderAmount.Should().Be(20m);
        dto.MaxOrderAmount.Should().Be(200m);
        dto.MaxUsageCount.Should().Be(5);
        dto.UsageCount.Should().Be(2);
        dto.IsActive.Should().BeTrue();
        dto.ValidFrom.Should().Be(rule.ValidFrom);
        dto.ValidUntil.Should().Be(rule.ValidUntil);
        dto.CreatedAt.Should().Be(rule.CreatedAt);
    }

    [Fact]
    public void ToDtoFromLookup_UserMissing_UsesUnknownFallback()
    {
        var rule = SampleRule();
        var empty = new Dictionary<Guid, (string? Email, string FirstName, string LastName)>();

        var dto = CustomerDiscountRuleMapper.ToDtoFromLookup(rule, empty);

        dto.UserEmail.Should().Be("Unknown");
        dto.UserName.Should().Be("Unknown");
    }

    [Fact]
    public void ToDtoFromLookup_PresentUserWithNullEmail_UsesEmptyString()
    {
        var rule = SampleRule();
        var lookup = new Dictionary<Guid, (string? Email, string FirstName, string LastName)>
        {
            [rule.UserId] = (null, "Ada", "Lovelace"),
        };

        var dto = CustomerDiscountRuleMapper.ToDtoFromLookup(rule, lookup);

        dto.UserEmail.Should().BeEmpty();
        dto.UserName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void ToDtoFromLookup_ComposesNameWithTrim()
    {
        var rule = SampleRule();
        var lookup = new Dictionary<Guid, (string? Email, string FirstName, string LastName)>
        {
            [rule.UserId] = ("solo@example.com", "Ada", ""),
        };

        var dto = CustomerDiscountRuleMapper.ToDtoFromLookup(rule, lookup);

        // "Ada ".Trim() == "Ada"
        dto.UserName.Should().Be("Ada");
    }

    // ----------------------------------------------------------- single shape

    [Fact]
    public void ToDtoFromUser_UserPresent_MapsUserFields()
    {
        var rule = SampleRule();
        (string? Email, string FirstName, string LastName)? user = ("ada@example.com", "Ada", "Lovelace");

        var dto = CustomerDiscountRuleMapper.ToDtoFromUser(rule, user);

        dto.Id.Should().Be(rule.Id);
        dto.UserEmail.Should().Be("ada@example.com");
        dto.UserName.Should().Be("Ada Lovelace");
        dto.DiscountType.Should().Be("Percentage");
    }

    [Fact]
    public void ToDtoFromUser_UserMissing_UsesUnknownFallback()
    {
        var rule = SampleRule();

        var dto = CustomerDiscountRuleMapper.ToDtoFromUser(rule, null);

        dto.UserEmail.Should().Be("Unknown");
        dto.UserName.Should().Be("Unknown");
    }

    [Fact]
    public void ToDtoFromUser_PresentUserWithNullEmail_UsesUnknown()
    {
        var rule = SampleRule();
        (string? Email, string FirstName, string LastName)? user = (null, "Ada", "Lovelace");

        var dto = CustomerDiscountRuleMapper.ToDtoFromUser(rule, user);

        // Single-shape fallback differs from the list shape: null email -> "Unknown".
        dto.UserEmail.Should().Be("Unknown");
        dto.UserName.Should().Be("Ada Lovelace");
    }

    // ------------------------------------------------------------ DTO -> entity

    [Fact]
    public void ToEntity_Create_MapsRequestFields()
    {
        var userId = Guid.NewGuid();
        var dto = new CreateCustomerDiscountRuleDto
        {
            UserId = userId,
            Name = "New Rule",
            DiscountType = "ignored-by-mapper",
            DiscountValue = 25m,
            MinOrderAmount = 10m,
            MaxOrderAmount = 100m,
            MaxUsageCount = 4,
            IsActive = true,
            ValidFrom = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidUntil = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var entity = CustomerDiscountRuleMapper.ToEntity(dto, DiscountType.FixedAmount);

        entity.UserId.Should().Be(userId);
        entity.Name.Should().Be("New Rule");
        entity.DiscountType.Should().Be(DiscountType.FixedAmount);
        entity.DiscountValue.Should().Be(25m);
        entity.MinOrderAmount.Should().Be(10m);
        entity.MaxOrderAmount.Should().Be(100m);
        entity.MaxUsageCount.Should().Be(4);
        entity.IsActive.Should().BeTrue();
        entity.ValidFrom.Should().Be(dto.ValidFrom);
        entity.ValidUntil.Should().Be(dto.ValidUntil);
        entity.CreatedBy.Should().Be("System");
    }

    [Fact]
    public void ToEntity_Update_MapsIdAndFields_ButNotUserId()
    {
        var id = Guid.NewGuid();
        var dto = new UpdateCustomerDiscountRuleDto
        {
            Name = "Edited Rule",
            DiscountType = "ignored-by-mapper",
            DiscountValue = 30m,
            MinOrderAmount = 15m,
            IsActive = false,
        };

        var entity = CustomerDiscountRuleMapper.ToEntity(id, dto, DiscountType.Percentage);

        entity.Id.Should().Be(id);
        entity.UserId.Should().Be(Guid.Empty); // update carries no UserId
        entity.Name.Should().Be("Edited Rule");
        entity.DiscountType.Should().Be(DiscountType.Percentage);
        entity.DiscountValue.Should().Be(30m);
        entity.MinOrderAmount.Should().Be(15m);
        entity.IsActive.Should().BeFalse();
        entity.CreatedBy.Should().Be("System");
    }
}
