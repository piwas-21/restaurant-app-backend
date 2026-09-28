using FluentAssertions;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

public class MenuSectionVariationValidatorTests
{
    [Fact]
    public void Legacy_snapshot_preserves_repeat_setting_before_structure_validation()
    {
        var sectionId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new MenuSection
        {
            Id = sectionId,
            Name = "Meats",
            IsRequired = true,
            MinSelection = 3,
            MaxSelection = 3,
            AllowRepeatedItems = true,
            CreatedBy = "test"
        };
        var legacySnapshot = new MenuSectionDto
        {
            Id = sectionId,
            Name = "Meats",
            IsRequired = true,
            MinSelection = 3,
            MaxSelection = 3,
            Items = [new MenuSectionItemDto { ProductId = productId }]
        };

        var effective = MenuSectionVariationValidator
            .ResolveExistingRepeatSettings([legacySnapshot], [existing]).ToList();

        effective.Single().AllowRepeatedItems.Should().BeTrue();
        var validation = () => MenuSectionIntegrityRule.ValidateStructure(effective);
        validation.Should().NotThrow();
    }
}
