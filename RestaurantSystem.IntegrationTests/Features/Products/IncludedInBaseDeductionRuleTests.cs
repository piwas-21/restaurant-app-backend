using FluentAssertions;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// backend #432 — the catalogue may not describe a product whose optional, included-in-base
/// ingredients cost more than the product itself, because
/// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> DEDUCTS every one of them that
/// is not selected, and #430 made an empty selection reachable by an anonymous
/// <c>POST /api/orders</c>.
///
/// <para>
/// <b>The fixture is the live catalogue, not an invention.</b> `Lebanese Plate` is a real RUMI
/// product measured on prod 2026-08-28: CHF 22.90, built as the exact sum of its seven mezze,
/// 22.90 to the cent (<c>docs/plans/_research/included-in-base-deduction-exposure.md</c>). It is the
/// reason the rule is <c>&gt;</c> and not <c>&gt;=</c>, and the reason a boundary product has its
/// own accepted-case test: a <c>&gt;=</c> rule would make a product that is live and correct today
/// unsaveable, which is a regression rather than a guard.
/// </para>
///
/// <para>
/// <b>Two of these tests are ORACLES, not arithmetic.</b> Asserting a number I derived myself would
/// only prove that the rule agrees with the understanding that wrote it. So the deduction is
/// asserted EQUAL to what the real pricing service produces for the same rows with an empty
/// selection — if either side moves, they stop agreeing.
/// </para>
/// </summary>
public class IncludedInBaseDeductionRuleTests
{
    // The seven mezze of the live product, in catalogue order. 4.50 + 4.50 + 1.50 + 1.20 + 4.50 +
    // 1.70 + 5.00 = 22.90.
    private static readonly decimal[] MezzePrices = [4.50m, 4.50m, 1.50m, 1.20m, 4.50m, 1.70m, 5.00m];
    private const decimal PlatePrice = 22.90m;

    // ---- fixtures ------------------------------------------------------------------------------

    private static ProductIngredientDto Included(decimal price, string name = "Mezze") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsOptional = true,
        IsIncludedInBasePrice = true,
        IsActive = true,
        Price = price,
        MaxQuantity = 3,
    };

    private static List<ProductIngredientDto> Mezze(decimal extra = 0m)
    {
        var rows = MezzePrices.Select(p => Included(p)).ToList();
        rows[0].Price += extra;
        return rows;
    }

    private static ValidationResult ValidateCreate(
        decimal basePrice,
        List<ProductIngredientDto>? ingredients,
        List<CreateProductVariationDto>? variations = null,
        bool hideBaseProduct = false)
    {
        var categoryId = Guid.NewGuid();
        return new CreateProductCommandValidator().Validate(new CreateProductCommand(
            Name: "Lebanese Plate", Description: null, BasePrice: basePrice, IsActive: true,
            IsAvailable: true, IsSpecial: false, PreparationTimeMinutes: 10,
            Type: ProductType.MainItem, KitchenType: KitchenType.None, Ingredients: null,
            Allergens: null, DisplayOrder: 0, CategoryIds: [categoryId],
            PrimaryCategoryId: categoryId, Variations: variations, SuggestedSideItemIds: null,
            DetailedIngredients: ingredients,
            Content: new ProductDescriptionsDto { ["en"] = new() { Name = "n", Description = "d" } },
            HideBaseProduct: hideBaseProduct));
    }

    private static ValidationResult ValidateUpdate(
        decimal basePrice,
        List<ProductIngredientDto>? ingredients,
        List<UpdateProductVariationDto>? variations = null,
        bool hideBaseProduct = false)
    {
        var categoryId = Guid.NewGuid();
        return new UpdateProductCommandValidator().Validate(new UpdateProductCommand(
            Id: Guid.NewGuid(), Name: "Lebanese Plate", Description: null, BasePrice: basePrice,
            IsActive: true, IsAvailable: true, IsSpecial: false, PreparationTimeMinutes: 10,
            Type: ProductType.MainItem, KitchenType: KitchenType.None, Ingredients: null,
            Allergens: null, DisplayOrder: 0, CategoryIds: [categoryId],
            PrimaryCategoryId: categoryId, Variations: variations, SuggestedSideItemIds: null,
            DetailedIngredients: ingredients, MenuDefinition: null, Content: null,
            HideBaseProduct: hideBaseProduct));
    }

    private static bool Refused(ValidationResult result) =>
        result.Errors.Exists(e => e.PropertyName == "DetailedIngredients"
                                  && e.ErrorMessage.Contains("included in the base price total",
                                      StringComparison.Ordinal));

    // ---- the live boundary, in both directions -------------------------------------------------

    /// <summary>
    /// The regression guard for the `&gt;` decision. `Lebanese Plate` exists, is correct, and sits
    /// at exactly 100 % of its own budget. A rule that refused it would break a live catalogue on
    /// the next save of a product nobody was editing to change.
    /// </summary>
    [Fact]
    public void TheLiveProductThatSitsExactlyOnTheBoundary_IsAccepted()
    {
        Refused(ValidateCreate(PlatePrice, Mezze())).Should().BeFalse(
            "equality prices the line at 0.00, not below — and it is what the configuration means: "
            + "the plate IS its seven mezze");
    }

    /// <summary>One CHF 0.01 rise in one mezze is the whole distance between safe and negative.</summary>
    [Fact]
    public void OneCentOverTheBoundary_IsRefused()
    {
        Refused(ValidateCreate(PlatePrice, Mezze(extra: 0.01m))).Should().BeTrue();
    }

    /// <summary>
    /// The same payload on PUT. This repo has already paid for a rule that existed on CREATE only —
    /// a 500-character variation name was a clean 400 on POST and reached the database on PUT
    /// (S4, backend analysis §9 defect 1) — and an admin edits far more often than they create.
    /// </summary>
    [Fact]
    public void OneCentOverTheBoundary_IsRefusedOnUpdateToo()
    {
        Refused(ValidateUpdate(PlatePrice, Mezze(extra: 0.01m))).Should().BeTrue();
    }

    /// <summary>The admin's fix is one of two specific edits, so the message names both numbers.</summary>
    [Fact]
    public void TheRefusal_NamesTheBudgetTheOverspendAndTheShortfall()
    {
        var message = ValidateCreate(PlatePrice, Mezze(extra: 0.10m))
            .Errors.Single(e => e.PropertyName == "DetailedIngredients").ErrorMessage;

        message.Should().Contain("23.00").And.Contain("22.90").And.Contain("0.10");
    }

    // ---- which rows count ----------------------------------------------------------------------

    [Fact]
    public void ARowThatIsNotOptional_DoesNotCountTowardTheDeduction()
    {
        var rows = Mezze(extra: 5m);
        rows[0].IsOptional = false;

        Refused(ValidateCreate(PlatePrice, rows)).Should().BeFalse(
            "a required ingredient can never be deselected, so it is never deducted");
    }

    [Fact]
    public void ARowThatIsNotIncludedInTheBasePrice_DoesNotCountTowardTheDeduction()
    {
        var rows = Mezze(extra: 5m);
        rows[0].IsIncludedInBasePrice = false;

        Refused(ValidateCreate(PlatePrice, rows)).Should().BeFalse("a paid extra is added, never deducted");
    }

    [Fact]
    public void AnInactiveRow_DoesNotCountTowardTheDeduction()
    {
        var rows = Mezze(extra: 5m);
        rows[0].IsActive = false;

        Refused(ValidateCreate(PlatePrice, rows)).Should().BeFalse();
    }

    [Fact]
    public void AProductWithNoIngredientsAtAll_IsAccepted()
    {
        Refused(ValidateCreate(PlatePrice, ingredients: null)).Should().BeFalse();
    }

    // ---- variations tighten the budget ---------------------------------------------------------

    private static CreateProductVariationDto Variation(decimal priceModifier, bool isActive = true) =>
        new("Small", null, priceModifier, isActive, 0, null);

    /// <summary>
    /// `ResolvePricing` returns `BasePrice + PriceModifier`, and a modifier may be NEGATIVE — so the
    /// budget is the cheapest way the line can be sold, not the base price. Refused here even though
    /// the deduction is comfortably under the base.
    /// </summary>
    [Fact]
    public void ANegativeVariationThatFallsBelowTheDeduction_IsRefused()
    {
        Refused(ValidateCreate(10m, [Included(8m)], [Variation(-3m)])).Should().BeTrue(
            "the small size sells for 7.00 and would price at -1.00");
    }

    [Fact]
    public void AnINACTIVEVariationDoesNotTightenTheBudget()
    {
        Refused(ValidateCreate(10m, [Included(8m)], [Variation(-3m, isActive: false)])).Should().BeFalse(
            "an inactive variation cannot be ordered, so it cannot be underpriced");
    }

    /// <summary>
    /// With the base row hidden the bare product is not orderable at all, so the budget is the
    /// cheapest VARIATION — which here is more generous than the base price, not less.
    /// </summary>
    [Fact]
    public void HideBaseProduct_MeasuresAgainstTheCheapestVariationInstead()
    {
        Refused(ValidateCreate(10m, [Included(12m)], [Variation(5m)], hideBaseProduct: true))
            .Should().BeFalse("only the 15.00 variation can be bought");

        Refused(ValidateCreate(10m, [Included(12m)], [Variation(5m)], hideBaseProduct: false))
            .Should().BeTrue("the 10.00 base row can still be bought, and 12.00 breaks it");
    }

    // ---- oracles: the rule's arithmetic IS the money path's ------------------------------------

    private static BasketPricingService PricingService() =>
        new(new Mock<ICustomerDiscountService>(MockBehavior.Strict).Object,
            Options.Create(new OrderSettings()),
            NullLogger<BasketPricingService>.Instance);

    private static List<ProductIngredient> AsEntities(IEnumerable<ProductIngredientDto> rows) =>
        rows.Select(r => new ProductIngredient
        {
            Id = r.Id!.Value,
            Name = r.Name,
            IsOptional = r.IsOptional,
            IsIncludedInBasePrice = r.IsIncludedInBasePrice,
            IsActive = r.IsActive,
            Price = r.Price,
            MaxQuantity = r.MaxQuantity,
            Kind = r.Kind,
            CreatedBy = "test",
        }).ToList();

    /// <summary>
    /// ORACLE. Not "the sum is 22.90" — that is my own arithmetic restated. This says the number the
    /// validator budgets against is EXACTLY the number the pricing service subtracts when a guest
    /// deselects everything. Nothing here computes a deduction twice; one side is measured.
    /// </summary>
    [Fact]
    public void TheRulesDeduction_IsTheDeductionTheMoneyPathActuallyApplies()
    {
        var rows = Mezze();

        var actual = PricingService().CalculateIngredientCustomizationPrice(
            AsEntities(rows), selectedIngredientIds: [], ingredientQuantities: null);

        // CONTROL first: "A == B" is also satisfied by two paths that are identically wrong, and by
        // two that both return zero. 22.90 is the figure MEASURED on the live product, so this pins
        // that the fixture really moves money before the parity below means anything.
        actual.Should().Be(-PlatePrice);

        IncludedInBaseDeductionRule.MaxDeduction(rows).Should().Be(-actual);
    }

    /// <summary>
    /// ORACLE, and the sauce half of it: a free-sauce allowance can only ever remove a CHARGE this
    /// same loop added, so it cannot widen a deduction. If that ever stops being true, the budget
    /// this rule enforces becomes too small and this test says so.
    /// </summary>
    [Fact]
    public void ASauceAllowance_CannotWidenTheDeduction()
    {
        var rows = Mezze();
        foreach (var row in rows)
        {
            row.Kind = IngredientKind.Sauce;
        }

        var entities = AsEntities(rows);
        var everySauce = rows.Select(r => r.Id!.Value).ToList();
        var twoOfEach = rows.ToDictionary(r => r.Id!.Value, _ => 2);

        // CONTROL: the allowance must actually be LIVE on this fixture, or "it did not widen the
        // deduction" would be true for the boring reason. Selecting two of every sauce charges for
        // the second unit of each; three units of that charge are then waived.
        var chargedWithoutAllowance = PricingService().CalculateIngredientCustomizationPrice(
            entities, everySauce, twoOfEach);
        var chargedWithAllowance = PricingService().CalculateIngredientCustomizationPrice(
            entities, everySauce, twoOfEach, sauceIncludedFree: 3);
        chargedWithAllowance.Should().BeLessThan(chargedWithoutAllowance,
            "the free-sauce rule is reachable with this fixture");

        var actual = PricingService().CalculateIngredientCustomizationPrice(
            entities, selectedIngredientIds: [], ingredientQuantities: null, sauceIncludedFree: 3);

        actual.Should().Be(-PlatePrice, "control: the deselect-everything case still moves money");
        IncludedInBaseDeductionRule.MaxDeduction(rows).Should().Be(-actual);
    }

    /// <summary>
    /// ORACLE, on the accepted boundary case: the product the rule lets through prices to exactly
    /// zero and not one cent below. This is the claim the `&gt;` decision rests on, checked against
    /// the real service rather than asserted.
    /// </summary>
    [Fact]
    public void TheAcceptedBoundaryProduct_PricesToExactlyZeroAndNotBelow()
    {
        var rows = Mezze();
        Refused(ValidateCreate(PlatePrice, rows)).Should().BeFalse("the control: this product saves");

        var customization = PricingService().CalculateIngredientCustomizationPrice(
            AsEntities(rows), selectedIngredientIds: [], ingredientQuantities: null);

        // itemTotal = unitPrice * quantity + customization (OrderItemFactory), worst case quantity 1.
        (PlatePrice + customization).Should().Be(0m);
    }

    /// <summary>And one cent over, the same line is negative — which is the harm being prevented.</summary>
    [Fact]
    public void OneCentOverTheBoundary_ActuallyPricesNegative()
    {
        var rows = Mezze(extra: 0.01m);

        var customization = PricingService().CalculateIngredientCustomizationPrice(
            AsEntities(rows), selectedIngredientIds: [], ingredientQuantities: null);

        (PlatePrice + customization).Should().Be(-0.01m);
        Refused(ValidateCreate(PlatePrice, rows)).Should().BeTrue("and the catalogue now refuses it");
    }
}
