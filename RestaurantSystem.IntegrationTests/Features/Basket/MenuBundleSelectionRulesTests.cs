using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>Pure hostile coverage for the menu section rule shared by Basket and staff pricing.</summary>
public sealed class MenuBundleSelectionRulesTests
{
    private static readonly Guid MainProduct = Guid.NewGuid();
    private static readonly Guid DrinkProduct = Guid.NewGuid();

    private static (MenuSection Main, MenuSection Drinks) Sections()
    {
        var main = new MenuSection
        {
            Id = Guid.NewGuid(),
            Name = "Main",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = nameof(MenuBundleSelectionRulesTests)
        };
        main.Items.Add(new MenuSectionItem
        {
            ProductId = MainProduct,
            AdditionalPrice = 3.00m,
            MenuSectionId = main.Id,
            CreatedBy = nameof(MenuBundleSelectionRulesTests)
        });

        var drinks = new MenuSection
        {
            Id = Guid.NewGuid(),
            Name = "Drinks",
            IsRequired = false,
            MinSelection = 2,
            MaxSelection = 2,
            CreatedBy = nameof(MenuBundleSelectionRulesTests)
        };
        drinks.Items.Add(new MenuSectionItem
        {
            ProductId = DrinkProduct,
            AdditionalPrice = 1.50m,
            MenuSectionId = drinks.Id,
            CreatedBy = nameof(MenuBundleSelectionRulesTests)
        });
        return (main, drinks);
    }

    [Fact]
    public void Sums_each_membership_price_using_per_unit_selection_quantities()
    {
        var sections = Sections();
        var total = MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            new List<SelectedMenuOptionDto>
            {
                new() { SectionId = sections.Main.Id, ItemId = MainProduct },
                new() { SectionId = sections.Drinks.Id, ItemId = DrinkProduct, Quantity = 2 }
            });

        total.Should().Be(6.00m);
    }

    [Fact]
    public void Enforces_required_minimum_but_keeps_optional_minimum_semantics_of_basket()
    {
        var sections = Sections();
        var act = () => MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            Array.Empty<SelectedMenuOptionDto>());

        act.Should().Throw<BadRequestException>()
            .WithMessage("Section 'Main' requires at least 1 selection(s)");

        var optionalMinimum = () => MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            new[] { new SelectedMenuOptionDto { SectionId = sections.Main.Id, ItemId = MainProduct } });
        optionalMinimum.Should().NotThrow("Basket only enforces MinSelection for required sections");
    }

    [Fact]
    public void Enforces_maximum_by_number_of_selected_options_not_sum_of_quantities()
    {
        var sections = Sections();
        var act = () => MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            new List<SelectedMenuOptionDto>
            {
                new() { SectionId = sections.Main.Id, ItemId = MainProduct },
                new() { SectionId = sections.Main.Id, ItemId = MainProduct }
            });

        act.Should().Throw<BadRequestException>()
            .WithMessage("Section 'Main' allows at most 1 selection(s)");
    }

    [Fact]
    public void Rejects_unknown_or_wrong_section_membership_before_pricing()
    {
        var sections = Sections();
        var wrongSection = () => MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            new[] { new SelectedMenuOptionDto { SectionId = sections.Main.Id, ItemId = DrinkProduct } });
        var unknownSection = () => MenuBundleSelectionRules.ResolveSectionItem(
            new[] { sections.Main, sections.Drinks }, Guid.NewGuid(), MainProduct);

        wrongSection.Should().Throw<NotFoundException>();
        unknownSection.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Rejects_non_positive_option_quantities()
    {
        var sections = Sections();
        var act = () => MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            new[] { sections.Main, sections.Drinks },
            new[] { new SelectedMenuOptionDto { SectionId = sections.Main.Id, ItemId = MainProduct, Quantity = 0 } });

        act.Should().Throw<BadRequestException>()
            .WithMessage("Invalid quantity for item in section 'Main'");
    }
}
