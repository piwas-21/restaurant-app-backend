using FluentAssertions;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

// Issue #156 (slice 4b): MenuBundleMapper is the single Product(Type=Menu) -> MenuBundleDto mapper
// for the bundle list + detail queries. Pins the unified projection: each section item carries its
// per-option DetailedIngredients (the full nested tree the drill-in needs — the detail query used to
// omit these), while the dead per-option SuggestedSideItems (removed in slice 1) are no longer
// projected. Pure static mapper — no DB.
public class MenuBundleMapperTests
{
    [Fact]
    public void MapsPerOptionDetailedIngredients_AndDropsDeadSuggestedSideItems()
    {
        var childProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Cola",
            BasePrice = 3m,
            Type = ProductType.MainItem,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        childProduct.DetailedIngredients.Add(new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = childProduct.Id,
            Name = "Ice",
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var section = new MenuSection
        {
            Id = Guid.NewGuid(),
            Name = "Drink",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            ProductId = childProduct.Id,
            Product = childProduct,
            AdditionalPrice = 1m,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var menuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        menuDefinition.Sections.Add(section);

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Combo",
            BasePrice = 12m,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            MenuDefinition = menuDefinition,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var dto = MenuBundleMapper.MapToMenuBundleDto(bundle, "https://cdn.example");

        var item = dto.MenuDefinition!.Sections.Single().Items.Single();
        item.DetailedIngredients.Should().ContainSingle().Which.Name.Should().Be("Ice");
        item.SuggestedSideItems.Should().BeNull("the dead per-option suggested sides are no longer projected");
    }
}
