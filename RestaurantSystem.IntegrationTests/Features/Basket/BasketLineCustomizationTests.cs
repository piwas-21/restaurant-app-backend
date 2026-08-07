using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// The rule extracted in #313 so the add path and the login merge stop disagreeing about what "the
// same line" means. It had ZERO tests of its own before the extraction — it was a private method of
// BasketService, reachable only through add-to-basket — so these exist because moving code without
// covering it is a claim, not a fact.
//
// Pure: no DB, no HTTP. LoginMergeCustomizationTests drives the same rule through the real merge.
public class BasketLineCustomizationTests
{
    private static readonly Guid Onion = Guid.NewGuid();
    private static readonly Guid Cheese = Guid.NewGuid();
    private static readonly Guid Cola = Guid.NewGuid();

    // BasketItem inherits a `required CreatedBy`, so every fixture goes through here rather than
    // repeating audit noise that has nothing to do with the rule under test.
    private static BasketItem Line(
        string? instructions = null,
        List<Guid>? selected = null,
        List<Guid>? added = null,
        string? sidesJson = null,
        string? quantitiesJson = null) => new()
        {
            Id = Guid.NewGuid(),
            CreatedBy = "test",
            SpecialInstructions = instructions,
            SelectedIngredients = selected,
            AddedIngredients = added,
            SelectedSideItemsJson = sidesJson,
            IngredientQuantitiesJson = quantitiesJson,
        };

    // Rows are never expected to be unreadable here, so a callback firing means the test's fixture is
    // wrong rather than the rule — except in the undecidable tests, which assert it fires.
    private static BasketLineCustomization? Row(BasketItem row, params BasketItem[] children) =>
        BasketLineCustomization.FromRow(row, children, (_, _) => { });

    private static bool Same(
        (BasketItem Parent, BasketItem[] Children) a, (BasketItem Parent, BasketItem[] Children) b) =>
        BasketLineCustomization.AreSame(Row(a.Parent, a.Children), Row(b.Parent, b.Children));

    // A bundle option as BuildMenuItemAsync stores it: LINE-ABSOLUTE quantity (parent qty x per-unit
    // count) against a per-unit UnitPrice, and no side items of its own.
    private static BasketItem Option(Guid productId, int lineAbsoluteQuantity, Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        CreatedBy = "test",
        ProductId = productId,
        ParentBasketItemId = parentId,
        Quantity = lineAbsoluteQuantity,
    };

    private static (BasketItem Parent, BasketItem[] Children) Bundle(int quantity, params (Guid Product, int PerUnit)[] options)
    {
        var parent = Line();
        parent.Quantity = quantity;
        return (parent, options.Select(o => Option(o.Product, quantity * o.PerUnit, parent.Id)).ToArray());
    }

    private static bool Same(BasketItem a, BasketItem b) =>
        BasketLineCustomization.AreSame(Row(a), Row(b));

    private static bool Same(BasketItem row, AddToBasketDto request) =>
        BasketLineCustomization.AreSame(Row(row), BasketLineCustomization.FromRequest(request));

    // ---- The two lines that matter to #313 ------------------------------------------------------

    [Fact]
    public void TwoPlainRows_AreTheSameLine()
    {
        Same(Line(), Line()).Should().BeTrue();
    }

    [Fact]
    public void ACustomisedRow_IsNotThePlainRow()
    {
        var customised = Line(sidesJson: $$"""[{"Id":"{{Cola}}","Quantity":1}]""");

        Same(customised, Line()).Should().BeFalse(
            "this is the merge defect: a side item is a choice, and identity alone cannot see it");
    }

    [Fact]
    public void RowsDifferingOnlyBySpecialInstructions_AreDifferentLines()
    {
        Same(Line(instructions: "No ice"), Line()).Should().BeFalse();
    }

    [Fact]
    public void NullAndEmptyInstructions_AreTheSame()
    {
        Same(Line(instructions: null), Line(instructions: ""))
            .Should().BeTrue("the add path has always coalesced both to the empty string");
    }

    [Fact]
    public void SelectedIngredientOrder_DoesNotMatter()
    {
        var a = Line(selected: new List<Guid> { Onion, Cheese });
        var b = Line(selected: new List<Guid> { Cheese, Onion });

        Same(a, b).Should().BeTrue();
    }

    [Fact]
    public void ADifferentSelectedIngredient_IsADifferentLine()
    {
        var a = Line(selected: new List<Guid> { Onion });
        var b = Line(selected: new List<Guid> { Cheese });

        Same(a, b).Should().BeFalse();
    }

    [Fact]
    public void ADifferentSideQuantity_IsADifferentLine()
    {
        var one = Line(sidesJson: $$"""[{"Id":"{{Cola}}","Quantity":1}]""");
        var two = Line(sidesJson: $$"""[{"Id":"{{Cola}}","Quantity":2}]""");

        Same(one, two).Should().BeFalse();
    }

    // ---- Undecidable: the property that must never degrade into "equal" -------------------------

    [Fact]
    public void AnUnreadableSideItemColumn_NeverMatches_NotEvenItself()
    {
        var corrupt = Line(sidesJson: "{ not json");

        Same(corrupt, Line()).Should().BeFalse();
        Same(corrupt, corrupt).Should().BeFalse(
            "undecidable is not equality — two rows nobody can read are not thereby the same line");
    }

    [Fact]
    public void AnUnreadableQuantitiesColumn_NeverMatches()
    {
        var corrupt = Line(quantitiesJson: "[[[");

        Same(corrupt, Line()).Should().BeFalse();
    }

    [Fact]
    public void AnUnreadableColumn_ReportsWhichOne()
    {
        var reported = new List<string>();
        BasketLineCustomization.FromRow(
            Line(sidesJson: "{ not json"),
            Array.Empty<BasketItem>(),
            (_, what) => reported.Add(what));

        reported.Should().ContainSingle().Which.Should().Be("side items",
            "the operator has to know which column to look at — this is the log the add path already emitted");
    }

    // The one behaviour change against the private rule this replaced: it short-circuited on an empty
    // selection BEFORE parsing, so this row used to dedup. Nothing in the codebase writes an
    // unparseable column, but #188 exists because they turn up anyway — and a second line is the
    // conservative answer this class gives everywhere else.
    [Fact]
    public void AnUnreadableColumn_WithNothingSelected_StillDoesNotMatch()
    {
        Same(Line(quantitiesJson: "{ not json"), Line()).Should().BeFalse();
    }

    [Fact]
    public void AnAbsentColumn_IsReadableAndEmpty()
    {
        var key = Row(Line(sidesJson: null, quantitiesJson: ""));

        key.Should().NotBeNull("a missing selection is a comparable state, unlike an unparseable one");
        BasketLineCustomization.AreSame(key, Row(Line())).Should().BeTrue(
            "and it is EMPTY, not merely non-null — the name promised that and nothing checked it");
    }

    // ---- The request side, and the two normalisations that differ by source ---------------------

    [Fact]
    public void ARequestedSideWithZeroQuantity_IsDropped_MatchingWhatWouldBePersisted()
    {
        var plainRow = Line();
        var request = new AddToBasketDto
        {
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = Cola, Quantity = 0 } }
        };

        Same(plainRow, request).Should().BeTrue(
            "BuildRegularItemAsync would not persist it, so it cannot make this a different line");
    }

    [Fact]
    public void ARequestedSideWithAPositiveQuantity_MatchesTheStoredRow()
    {
        var row = Line(sidesJson: $$"""[{"Id":"{{Cola}}","Quantity":2}]""");
        var request = new AddToBasketDto
        {
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = Cola, Quantity = 2 } }
        };

        Same(row, request).Should().BeTrue();
    }

    // ---- Effective quantity: the rule that lets a client omit the default -----------------------

    [Fact]
    public void AnOmittedQuantity_MatchesAnExplicitOne()
    {
        var row = Line(selected: new List<Guid> { Onion }, quantitiesJson: $$"""{"{{Onion}}":1}""");
        var request = new AddToBasketDto { SelectedIngredients = new List<Guid> { Onion } };

        Same(row, request).Should().BeTrue("an effective quantity of 1 is the default on both sides");
    }

    [Fact]
    public void AnExplicitQuantityAboveOne_IsADifferentLine()
    {
        var row = Line(selected: new List<Guid> { Onion }, quantitiesJson: $$"""{"{{Onion}}":2}""");
        var request = new AddToBasketDto { SelectedIngredients = new List<Guid> { Onion } };

        Same(row, request).Should().BeFalse("double onion is not single onion (#155)");
    }

    // The backfill writes an explicit 0 for every ingredient the guest did NOT select, which is what
    // OrderMappingService turns into "NO Cheese" on the ticket (#304). Those entries are not choices
    // about the SELECTED ingredients, and the selection sets already capture them — so they must not
    // make two otherwise-identical lines differ.
    [Fact]
    public void BackfilledZeroEntriesForDeselectedIngredients_DoNotSplitTheLine()
    {
        var row = Line(
            selected: new List<Guid> { Onion },
            quantitiesJson: $$"""{"{{Onion}}":1,"{{Cheese}}":0}""");
        var request = new AddToBasketDto
        {
            SelectedIngredients = new List<Guid> { Onion },
            IngredientQuantities = new Dictionary<Guid, int> { [Onion] = 1 },
        };

        Same(row, request).Should().BeTrue();
    }

    // A BACKFILLED zero with nothing selected is not a choice, so it must not split the line. The
    // fixture deliberately uses 0 and NOT an arbitrary value: at 0 this is the #304 backfill, and at
    // any other value it is a real choice — see the test below, which is the case an earlier version
    // of this rule got wrong while this one passed.
    [Fact]
    public void WithNothingSelected_ABackfilledZero_IsNotAChoice()
    {
        var row = Line(quantitiesJson: $$"""{"{{Cheese}}":0}""");
        var request = new AddToBasketDto();

        Same(row, request).Should().BeTrue();
    }

    // The hole an earlier version of this rule left open, and the reason quantities are no longer
    // projected through the selection. LineCustomizationBuilder persists an explicit client map
    // BEFORE its selection gate, so `{ ProductId, IngredientQuantities }` with no SelectedIngredients
    // is a row the add path really produces — and comparing only the selected ids made a double-onion
    // line identical to a plain one. On the merge path that meant the plain guest unit absorbed the
    // other line's customization price and the ticket printed a removal nobody asked for.
    [Fact]
    public void AQuantityWithNoSelection_IsStillAChoice()
    {
        var doubleOnion = Line(quantitiesJson: $$"""{"{{Onion}}":2}""");

        Same(doubleOnion, Line()).Should().BeFalse(
            "a quantity map with no selection is reachable, and it is a choice like any other");
    }

    // ---- Bundles: the composition lives on the CHILDREN, not the parent ------------------------
    //
    // BuildMenuItemAsync writes NO customization columns on a bundle parent — only instructions — so a
    // rule that reads the parent alone sees every bundle of one menu product as identical. Measured
    // before this was folded in: Combo+Cola and Combo+Sprite merged, the guest paid 26.00 for the
    // 22.00 they built, and their own rows were left on a soft-deleted basket.

    [Fact]
    public void TwoBundlesWithDifferentOptions_AreDifferentLines()
    {
        var cola = Guid.NewGuid();
        var sprite = Guid.NewGuid();

        Same(Bundle(1, (cola, 1)), Bundle(1, (sprite, 1))).Should().BeFalse();
    }

    [Fact]
    public void TwoBundlesWithTheSameOptions_AreTheSameLine()
    {
        var cola = Guid.NewGuid();

        Same(Bundle(1, (cola, 1)), Bundle(1, (cola, 1))).Should().BeTrue();
    }

    // The reason composition is normalised PER UNIT. A child's stored Quantity is line-absolute, so
    // the same build at different line quantities stores different numbers — and #305's merge case,
    // the same bundle in both baskets, has to keep merging.
    [Fact]
    public void TheSameBuildAtDifferentLineQuantities_IsTheSameLine()
    {
        var cola = Guid.NewGuid();

        Same(Bundle(1, (cola, 2)), Bundle(3, (cola, 2))).Should().BeTrue(
            "two drinks per bundle is two drinks per bundle whether you ordered one bundle or three");
    }

    [Fact]
    public void ADifferentPerUnitOptionCount_IsADifferentLine()
    {
        var cola = Guid.NewGuid();

        Same(Bundle(2, (cola, 1)), Bundle(2, (cola, 2))).Should().BeFalse();
    }

    [Fact]
    public void OptionOrder_DoesNotMatter()
    {
        var cola = Guid.NewGuid();
        var fries = Guid.NewGuid();

        Same(Bundle(1, (cola, 1), (fries, 1)), Bundle(1, (fries, 1), (cola, 1))).Should().BeTrue();
    }

    [Fact]
    public void ABundle_IsNeverTheSameLineAsARegularItem()
    {
        var cola = Guid.NewGuid();

        Same(Bundle(1, (cola, 1)), (Line(), Array.Empty<BasketItem>())).Should().BeFalse(
            "a childless row cannot be the bundle — this is also what stops a retyped bundle parent deduping into a plain add (#308)");
    }

    // A child count that does not divide by the parent's quantity cannot be reduced to a per-unit
    // figure. BundleChildQuantityScaler refuses to invent a number in exactly this state; so does this.
    [Fact]
    public void AnIndivisibleChildCount_IsUndecidable_AndNeverMatches()
    {
        var cola = Guid.NewGuid();
        var parent = Line();
        parent.Quantity = 2;
        var odd = (parent, new[] { Option(cola, 3, parent.Id) });

        Same(odd, odd).Should().BeFalse();
    }
}
