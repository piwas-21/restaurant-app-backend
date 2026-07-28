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

        var dto = MenuBundleMapper.MapToMenuBundleDto(bundle, "https://cdn.example", requestedOrderType: null);

        var item = dto.MenuDefinition!.Sections.Single().Items.Single();
        item.DetailedIngredients.Should().ContainSingle().Which.Name.Should().Be("Ice");
        item.SuggestedSideItems.Should().BeNull("the dead per-option suggested sides are no longer projected");
    }

    // §9.2: bundles had no availability at all — MenuBundleDto was never wired to
    // OrderTypeAvailability and no bundle command accepted a mask, so a restricted combo rendered as
    // fully orderable. These pin BOTH halves of the resolution the mapper is responsible for; the
    // include that feeds the inheritance half can only be pinned through the query, and is
    // (MenuBundleAvailabilityTests).
    [Theory]
    [InlineData(OrderType.DineIn, false)]
    [InlineData(OrderType.Takeaway, true)]
    public void ResolvesAvailabilityFromTheBundlesOwnMask(OrderType requested, bool expectedCanOrder)
    {
        var bundle = BundleWith(availableOrderTypes: (int)(OrderChannels.Takeaway | OrderChannels.Delivery));

        var dto = MenuBundleMapper.MapToMenuBundleDto(bundle, "https://cdn.example", requested);

        dto.Availability.CanOrder.Should().Be(expectedCanOrder);
        dto.Availability.InheritsOrderTypes.Should().BeFalse();
        dto.AvailableOrderTypes.Should().Be((int)(OrderChannels.Takeaway | OrderChannels.Delivery),
            "the admin editor round-trips the stored mask, not the resolved verdict");
    }

    [Fact]
    public void InheritsTheMaskOfThePrimaryCategory()
    {
        var bundle = BundleWith(availableOrderTypes: null);
        bundle.ProductCategories.Add(new ProductCategory
        {
            ProductId = bundle.Id,
            IsPrimary = true,
            Category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Combos",
                AvailableOrderTypes = (int)OrderChannels.Takeaway,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var dto = MenuBundleMapper.MapToMenuBundleDto(bundle, "https://cdn.example", OrderType.DineIn);

        dto.Availability.CanOrder.Should().BeFalse();
        dto.Availability.InheritsOrderTypes.Should().BeTrue();
        dto.Availability.AllowedOrderTypes.Should().Equal(OrderType.Takeaway);
        dto.AvailableOrderTypes.Should().BeNull("inheriting means the bundle stores no mask of its own");
    }

    [Fact]
    public void ResolvesUnrestrictedWhenNothingRestrictsTheBundle()
    {
        var dto = MenuBundleMapper.MapToMenuBundleDto(
            BundleWith(availableOrderTypes: null), "https://cdn.example", OrderType.DineIn);

        dto.Availability.CanOrder.Should().BeTrue();
        dto.Availability.AllowedOrderTypes.Should().HaveCount(3);
    }

    private static Product BundleWith(int? availableOrderTypes) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Combo",
        BasePrice = 12m,
        Type = ProductType.Menu,
        IsAvailable = true,
        AvailableOrderTypes = availableOrderTypes,
        Ingredients = new List<string>(),
        Allergens = new List<string>(),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };
}
