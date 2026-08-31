using FluentAssertions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

// Issue #156 (slice 4): ProductDtoMapper is the single Product -> ProductDto mapper shared by the
// product and menu-bundle create/update commands (replaced 1 full copy + 2 bundle-only subsets).
// Pins the unified behaviour: a regular product maps its detailed ingredients; a bundle maps its
// MenuDefinition and carries the product-specific collections EMPTY (not null) — the uniform,
// harmless response shape that replaced the old subset's omissions. Pure static mapper — no DB.
public class ProductDtoMapperTests
{
    [Fact]
    public void RegularProduct_MapsDetailedIngredients_AndHasNoMenuDefinition()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Pizza",
            BasePrice = 10m,
            Type = ProductType.MainItem,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Name = "Cheese",
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var dto = ProductDtoMapper.MapToProductDto(product);

        dto.DetailedIngredients.Should().ContainSingle().Which.Name.Should().Be("Cheese");
        dto.MenuDefinition.Should().BeNull();
    }

    [Theory]
    [InlineData(ProductType.Beverage)]
    [InlineData(ProductType.Dessert)]
    [InlineData(ProductType.MainItem)]
    public void SuggestedSideItem_MapsItsProductType(ProductType sideType)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Meal",
            BasePrice = 10m,
            Type = ProductType.MainItem,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var side = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Side",
            BasePrice = 2m,
            Type = sideType,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        product.SuggestedSideItems.Add(new ProductSideItem
        {
            Id = Guid.NewGuid(),
            MainProductId = product.Id,
            SideItemProductId = side.Id,
            SideItemProduct = side,
            IsRequired = false,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var dto = ProductDtoMapper.MapToProductDto(product);

        dto.SuggestedSideItems.Should().ContainSingle().Which.Type.Should().Be(sideType);
    }

    [Fact]
    public void Bundle_MapsMenuDefinition_AndCarriesProductCollectionsEmptyNotNull()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Combo",
            BasePrice = 15m,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            MenuDefinition = new MenuDefinition
            {
                Id = Guid.NewGuid(),
                IsAlwaysAvailable = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            }
        };

        var dto = ProductDtoMapper.MapToProductDto(product);

        dto.MenuDefinition.Should().NotBeNull();
        dto.MenuDefinition!.IsAlwaysAvailable.Should().BeTrue();
        // Uniform shape: a bundle carries the product-specific collections empty, not null.
        dto.DetailedIngredients.Should().NotBeNull().And.BeEmpty();
        dto.Variations.Should().NotBeNull().And.BeEmpty();
        dto.SuggestedSideItems.Should().NotBeNull().And.BeEmpty();
    }
}
