using FluentAssertions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

public sealed class CatalogOfferFamilyBuilderTests
{
    [Fact]
    public void Groups_a_product_and_its_menu_once_and_uses_anchor_categories()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Tacos", DisplayOrder = 2, CreatedBy = "test" };
        var anchor = Product("Tacos 1 Viande", 9m, ProductType.MainItem, category);
        var menu = Product("Menu Tacos 1 Viande", 12m, ProductType.Menu);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            Product = menu,
            IsAlwaysAvailable = true,
            CreatedBy = "test"
        };

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        families.Should().ContainSingle();
        families[0].Id.Should().Be(anchor.Id);
        families[0].CategoryIds.Should().ContainSingle().Which.Should().Be(category.Id);
        families[0].MenuOffers.Should().ContainSingle();
        families[0].MenuOffers[0].ProductId.Should().Be(menu.Id);
        families[0].StartingPrice.Should().Be(9m);
    }

    [Fact]
    public void Product_summary_exposes_explicit_offer_parent_links()
    {
        var anchor = Product("Tacos", 9m, ProductType.MainItem);
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Large",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);
        var menu = Product("Menu Tacos", 12m, ProductType.Menu);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            ParentOfferVariationId = variation.Id,
            Product = menu,
            CreatedBy = "test"
        };

        var summary = ProductSummaryMapper.MapToSummaryDto(menu, "", null);

        summary.ParentOfferProductId.Should().Be(anchor.Id);
        summary.ParentOfferVariationId.Should().Be(variation.Id);
    }

    [Fact]
    public void Keeps_an_invalid_parent_variation_menu_as_an_independent_fallback_card()
    {
        var anchor = Product("6 Nuggets", 6m, ProductType.MainItem);
        anchor.Variations.Add(new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "6 pieces",
            IsActive = true,
            CreatedBy = "test"
        });
        var menu = Product("Menu 6 Nuggets", 9m, ProductType.Menu);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            ParentOfferVariationId = Guid.NewGuid(),
            Product = menu,
            IsAlwaysAvailable = true,
            CreatedBy = "test"
        };

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        families.Should().HaveCount(2);
        families.Select(family => family.Id).Should().Contain(new[] { anchor.Id, menu.Id });
        families.Single(family => family.Id == anchor.Id).MenuOffers.Should().BeEmpty();
    }

    [Fact]
    public void Falls_back_to_independent_menu_when_anchor_is_unavailable()
    {
        var anchor = Product("Tacos", 8m, ProductType.MainItem);
        anchor.IsAvailable = false;
        var menu = Product("Menu Tacos", 11m, ProductType.Menu);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            Product = menu,
            CreatedBy = "test"
        };

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        families.Should().HaveCount(2);
        families.Single(family => family.Id == anchor.Id).MenuOffers.Should().BeEmpty();
        families.Single(family => family.Id == menu.Id).MenuOffers.Should().BeEmpty();
    }

    [Fact]
    public void Supports_variation_specific_targets_and_schedule_fallback()
    {
        var anchor = Product("Wings", 8m, ProductType.MainItem);
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "5 pieces",
            PriceModifier = 1m,
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);

        var menu = Product("Menu Wings", 11m, ProductType.Menu);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            ParentOfferVariationId = variation.Id,
            Product = menu,
            IsAlwaysAvailable = false,
            AvailableMonday = true,
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            CreatedBy = "test"
        };

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, null, null, DayOfWeek.Monday, new TimeSpan(19, 0, 0), "");

        families.Should().ContainSingle();
        families[0].MenuOffers.Should().ContainSingle();
        families[0].MenuOffers[0].ParentVariationId.Should().Be(variation.Id);
        families[0].MenuOffers[0].ScheduleAvailable.Should().BeTrue();
        families[0].StartingPrice.Should().Be(8m);
        families[0].Anchor.Variations![0].FinalPrice.Should().Be(9m);
    }

    [Fact]
    public void Category_filter_and_hidden_all_tab_placement_apply_to_anchors_only()
    {
        var hidden = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Internal",
            IsHiddenFromAllTab = true,
            CreatedBy = "test"
        };
        var visible = new Category { Id = Guid.NewGuid(), Name = "Public", CreatedBy = "test" };
        var anchor = Product("Dish", 10m, ProductType.MainItem, hidden, visible);
        var menu = Product("Menu Dish", 12m, ProductType.Menu, hidden);
        menu.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            ParentOfferProductId = anchor.Id,
            Product = menu,
            CreatedBy = "test"
        };

        var allFamilies = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");
        var hiddenTabFamilies = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, menu }, hidden.Id, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        allFamilies.Should().ContainSingle();
        allFamilies[0].CategoryIds.Should().Contain(new[] { visible.Id, hidden.Id });
        hiddenTabFamilies.Should().ContainSingle();
        hiddenTabFamilies[0].Id.Should().Be(anchor.Id);
    }

    private static Product Product(string name, decimal price, ProductType type, params Category[] categories)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            BasePrice = price,
            Type = type,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = [],
            CreatedBy = "test"
        };

        foreach (var category in categories)
        {
            product.ProductCategories.Add(new ProductCategory
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Product = product,
                CategoryId = category.Id,
                Category = category,
                IsPrimary = product.ProductCategories.Count == 0,
                DisplayOrder = product.ProductCategories.Count,
                CreatedBy = "test"
            });
        }

        return product;
    }
}
