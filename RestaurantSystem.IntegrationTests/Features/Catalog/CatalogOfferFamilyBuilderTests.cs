using System.Diagnostics;
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
    public void Keeps_one_unavailable_anchor_family_with_all_active_menu_children()
    {
        var anchor = Product("Tacos", 8m, ProductType.MainItem);
        anchor.IsAvailable = false;
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Large",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);
        var generic = Product("Menu Tacos", 11m, ProductType.Menu);
        generic.MenuDefinition = Definition(generic, anchor.Id);
        var large = Product("Menu Tacos Large", 13m, ProductType.Menu);
        large.MenuDefinition = Definition(large, anchor.Id, variation.Id);

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, generic, large }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        families.Should().ContainSingle();
        families[0].Id.Should().Be(anchor.Id);
        families[0].Anchor.IsAvailable.Should().BeFalse();
        families[0].Anchor.Availability.CanOrder.Should().BeFalse();
        families[0].MenuOffers.Should().HaveCount(2);
        families[0].MenuOffers.Select(offer => offer.ProductId)
            .Should().BeEquivalentTo(new[] { generic.Id, large.Id });
    }

    [Fact]
    public void Keeps_one_inactive_anchor_family_with_all_active_menu_children()
    {
        var anchor = Product("Tacos", 8m, ProductType.MainItem);
        anchor.IsActive = false;
        anchor.IsAvailable = false;
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Large",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);
        var generic = Product("Menu Tacos", 11m, ProductType.Menu);
        generic.MenuDefinition = Definition(generic, anchor.Id);
        var large = Product("Menu Tacos Large", 13m, ProductType.Menu);
        large.MenuDefinition = Definition(large, anchor.Id, variation.Id);

        var families = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, generic, large }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        families.Should().ContainSingle();
        families[0].Id.Should().Be(anchor.Id);
        families[0].Anchor.IsActive.Should().BeFalse();
        families[0].Anchor.Availability.CanOrder.Should().BeFalse();
        families[0].MenuOffers.Should().HaveCount(2);
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
        families[0].AnchorScheduleAvailable.Should().BeTrue();
        families[0].StartingPrice.Should().Be(8m);
        families[0].Anchor.Variations![0].FinalPrice.Should().Be(9m);
    }

    [Fact]
    public void Keeps_a_closed_menu_anchor_card_when_an_open_child_exists()
    {
        var anchor = Product("Lunch Combo", 10m, ProductType.Menu);
        anchor.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Product = anchor,
            IsAlwaysAvailable = false,
            AvailableMonday = true,
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            CreatedBy = "test"
        };
        var menu = Product("Lunch Combo Dinner", 12m, ProductType.Menu);
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
        families[0].AnchorScheduleAvailable.Should().BeFalse();
        families[0].Anchor.Availability.CanOrder.Should().BeTrue();
        families[0].MenuOffers.Should().ContainSingle();
        families[0].MenuOffers[0].ScheduleAvailable.Should().BeTrue();
        families[0].StartingPrice.Should().Be(12m);
    }

    [Fact]
    public void Allows_generic_and_variation_menu_children_of_a_standalone_bundle_anchor()
    {
        var anchor = Product("Family Combo", 10m, ProductType.Menu);
        anchor.MenuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Product = anchor,
            CreatedBy = "test"
        };
        var generic = Product("Family Combo Generic", 12m, ProductType.Menu);
        generic.MenuDefinition = Definition(generic, anchor.Id);
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Large",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);
        var large = Product("Family Combo Large", 14m, ProductType.Menu);
        large.MenuDefinition = Definition(large, anchor.Id, variation.Id);

        var family = CatalogOfferFamilyBuilder.Build(
            new[] { anchor, generic, large }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "")
            .Should().ContainSingle().Which;

        family.MenuOffers.Should().HaveCount(2);
        family.MenuOffers.Select(offer => offer.ParentVariationId)
            .Should().BeEquivalentTo(new Guid?[] { null, variation.Id });
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
        allFamilies[0].VisibleInAll.Should().BeTrue();
        hiddenTabFamilies.Should().ContainSingle();
        hiddenTabFamilies[0].Id.Should().Be(anchor.Id);
    }

    [Fact]
    public void Includes_hidden_only_family_in_all_but_keeps_it_on_its_category_tab()
    {
        var hidden = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Internal",
            IsHiddenFromAllTab = true,
            CreatedBy = "test"
        };
        var anchor = Product("Staff Special", 10m, ProductType.MainItem, hidden);

        var allFamilies = CatalogOfferFamilyBuilder.Build(
            new[] { anchor }, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");
        var hiddenTabFamilies = CatalogOfferFamilyBuilder.Build(
            new[] { anchor }, hidden.Id, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");
        var unrelatedCategoryFamilies = CatalogOfferFamilyBuilder.Build(
            new[] { anchor }, Guid.NewGuid(), null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");

        allFamilies.Should().ContainSingle();
        allFamilies[0].VisibleInAll.Should().BeFalse();
        allFamilies[0].CategoryIds.Should().ContainSingle().Which.Should().Be(hidden.Id);
        hiddenTabFamilies.Should().ContainSingle().Which.Id.Should().Be(anchor.Id);
        unrelatedCategoryFamilies.Should().BeEmpty();
    }

    [Fact]
    public void Indexes_children_before_mapping_a_bounded_catalogue()
    {
        const int familyCount = 3000;
        var products = new List<Product>(familyCount * 2);
        for (var index = 0; index < familyCount; index++)
        {
            var anchor = Product($"Dish {index}", 10m, ProductType.MainItem);
            var menu = Product($"Menu Dish {index}", 12m, ProductType.Menu);
            menu.MenuDefinition = Definition(menu, anchor.Id);
            products.Add(anchor);
            products.Add(menu);
        }

        var stopwatch = Stopwatch.StartNew();
        var families = CatalogOfferFamilyBuilder.Build(
            products, null, null, DayOfWeek.Monday, new TimeSpan(12, 0, 0), "");
        stopwatch.Stop();

        families.Should().HaveCount(familyCount);
        families.Select(family => family.Id).Should().OnlyHaveUniqueItems();
        families.Should().OnlyContain(family => family.MenuOffers.Count == 1);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
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

    private static MenuDefinition Definition(Product menu, Guid parentId, Guid? variationId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = menu.Id,
        ParentOfferProductId = parentId,
        ParentOfferVariationId = variationId,
        Product = menu,
        CreatedBy = "test"
    };
}
