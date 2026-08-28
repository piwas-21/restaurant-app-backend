using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// Slice S6 of SHARED-MODIFIERS-AND-SAUCES-PLAN (D10): <b><c>SauceIncludedFree</c> actually
/// prices.</b> A merchant asked for <i>"2 sauces free … if they tick a third it then adds a fee"</i>
/// and Square answered that it has no rule-based system for it; this is that rule, and it lives in
/// the ONE place ingredient money is written —
/// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rule in one sentence: price every row exactly as before, then give back the price of the
/// <c>N</c> most expensive sauce units this call actually CHARGED for.
/// </para>
/// <para>
/// <b>Most expensive first is a deliberate choice, and the third reason is the security one.</b> It
/// is the customer-friendly reading of "N sauces included"; it is deterministic; and it does not
/// depend on the order of the client-supplied selection array, which would otherwise make the order
/// of a JSON array a price lever. Equal prices fall back to <c>DisplayOrder</c> — the order the
/// guest sheet renders in — so the sheet's "Included" badge lands on the first row shown.
/// </para>
/// <para>
/// <b>Nothing here enforces <c>SauceMin</c>/<c>SauceMax</c>.</b> That is an S6 decision, not an
/// omission: the cap is a UI affordance, and money is safe without it because every sauce unit
/// beyond the allowance is charged in full. There is no 400 to test for.
/// </para>
/// </remarks>
public class SauceIncludedFreePricingTests
{
    private static BasketPricingService CreateSut() =>
        new(new Mock<ICustomerDiscountService>(MockBehavior.Strict).Object,
            Options.Create(new OrderSettings()),
            NullLogger<BasketPricingService>.Instance);

    private static ProductIngredient Row(
        string name,
        decimal price,
        int displayOrder,
        IngredientKind kind = IngredientKind.Ingredient,
        bool includedInBase = false,
        int maxQuantity = 3,
        bool isOptional = true,
        bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Price = price,
            DisplayOrder = displayOrder,
            Kind = kind,
            IsIncludedInBasePrice = includedInBase,
            MaxQuantity = maxQuantity,
            IsOptional = isOptional,
            IsActive = isActive,
            CreatedBy = "test"
        };

    // ---------------------------------------------------------------------------------------
    // The two reference implementations. The first is TODAY's rule, copied literally from the
    // pre-S6 method so the regression test compares against the shipped behaviour rather than
    // against the new code's opinion of it. The second adds the waiver on top and is what the
    // property test measures the service against.
    // ---------------------------------------------------------------------------------------

    /// <summary>The pre-S6 per-ingredient rule, verbatim. No sauce concept exists in here at all.</summary>
    private static decimal PriceAsItWasBeforeS6(
        IEnumerable<ProductIngredient> rows,
        IReadOnlyCollection<Guid> selected,
        Dictionary<Guid, int>? quantities)
    {
        decimal price = 0;
        foreach (var row in rows.Where(r => r.IsOptional && r.IsActive))
        {
            var isSelected = selected.Contains(row.Id);
            var quantity = quantities != null && quantities.TryGetValue(row.Id, out var q) ? q : 1;
            quantity = Math.Clamp(quantity, 0, row.MaxQuantity);

            if (row.IsIncludedInBasePrice)
            {
                if (!isSelected)
                {
                    price -= row.Price;
                }
                else if (quantity > 1)
                {
                    price += row.Price * (quantity - 1);
                }
            }
            else if (isSelected)
            {
                price += row.Price * quantity;
            }
        }

        return price;
    }

    /// <summary>
    /// The whole S6 rule, expressed independently of the service: today's delta, minus the
    /// <c>N</c> dearest sauce unit prices among the units today's rule billed for.
    /// </summary>
    private static decimal ReferenceRule(
        IEnumerable<ProductIngredient> rows,
        IReadOnlyCollection<Guid> selected,
        Dictionary<Guid, int>? quantities,
        int sauceIncludedFree)
    {
        var all = rows.ToList();
        var chargedSauceUnits = new List<ProductIngredient>();

        foreach (var row in all.Where(r => r.IsOptional && r.IsActive
                                           && r.Kind == IngredientKind.Sauce && r.Price > 0))
        {
            var isSelected = selected.Contains(row.Id);
            var quantity = quantities != null && quantities.TryGetValue(row.Id, out var q) ? q : 1;
            quantity = Math.Clamp(quantity, 0, row.MaxQuantity);

            // How many units of this row today's rule adds money for — nothing else is waivable.
            var billedUnits = row.IsIncludedInBasePrice
                ? (isSelected ? Math.Max(0, quantity - 1) : 0)
                : (isSelected ? quantity : 0);

            chargedSauceUnits.AddRange(Enumerable.Repeat(row, billedUnits));
        }

        var waiver = chargedSauceUnits
            .OrderByDescending(r => r.Price)
            .ThenBy(r => r.DisplayOrder)
            .ThenBy(r => r.Id)
            .Take(Math.Max(0, sauceIncludedFree))
            .Sum(r => r.Price);

        return PriceAsItWasBeforeS6(all, selected, quantities) - waiver;
    }

    // ---------------------------------------------------------------------------------------
    // The regression that protects every product on production. Every one of them carries
    // SauceIncludedFree = 0, because that is the column default the S5 migration wrote.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void NoFreeAllowance_PricesByteIdenticallyToToday()
    {
        var cheese = Row("Cheese", 1.50m, 0, includedInBase: true, maxQuantity: 2);
        var mushrooms = Row("Mushrooms", 2.00m, 1);
        var garlic = Row("Garlic Sauce", 0.50m, 2, IngredientKind.Sauce);
        var chilli = Row("Chilli Sauce", 1.20m, 3, IngredientKind.Sauce, maxQuantity: 2);
        var mayo = Row("Mayo", 0.80m, 4, IngredientKind.Sauce, includedInBase: true, maxQuantity: 2);
        var rows = new[] { cheese, mushrooms, garlic, chilli, mayo };
        var sut = CreateSut();

        // Every subset of the five rows, at every quantity 1..3 applied to all of them at once.
        foreach (var selection in Subsets(rows))
        {
            for (var quantity = 1; quantity <= 3; quantity++)
            {
                var ids = selection.Select(r => r.Id).ToList();
                var quantities = rows.ToDictionary(r => r.Id, _ => quantity);

                var today = PriceAsItWasBeforeS6(rows, ids, quantities);

                // 0 is the shipped default, and a negative value (which no product can hold, but a
                // future caller could pass) must be just as inert.
                sut.CalculateIngredientCustomizationPrice(rows, ids, quantities, 0)
                   .Should().Be(today);
                sut.CalculateIngredientCustomizationPrice(rows, ids, quantities, -3)
                   .Should().Be(today);
                sut.CalculateIngredientCustomizationPrice(rows, ids, quantities)
                   .Should().Be(today, "the parameter is defaulted, so no existing caller changes");
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // The waiver itself.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheWaiverTakesTheMostExpensiveChargedSauce()
    {
        var garlic = Row("Garlic Sauce", 0.50m, 0, IngredientKind.Sauce);
        var truffle = Row("Truffle Sauce", 2.50m, 1, IngredientKind.Sauce);
        var rows = new[] { garlic, truffle };
        var ids = new[] { garlic.Id, truffle.Id };

        // One free sauce: the guest keeps the dear one for nothing and pays for the cheap one.
        // Note the selection list is ordered garlic-first — the price must not follow it.
        CreateSut().CalculateIngredientCustomizationPrice(rows, ids, null, 1)
            .Should().Be(0.50m);

        // Two free: both are covered, and the delta is exactly zero — not negative.
        CreateSut().CalculateIngredientCustomizationPrice(rows, ids, null, 2)
            .Should().Be(0m);
    }

    [Fact]
    public void AnAllowanceLargerThanTheChargedSauces_NeverBecomesARefund()
    {
        var garlic = Row("Garlic Sauce", 0.50m, 0, IngredientKind.Sauce);
        var mushrooms = Row("Mushrooms", 2.00m, 1);
        var rows = new[] { garlic, mushrooms };

        // Ten free sauces against one 0.50 sauce and a 2.00 NON-sauce extra: the waiver can only
        // remove the 0.50 it charged. The mushrooms are never touched — the allowance is a SAUCE
        // allowance, not a discount on the line.
        CreateSut()
            .CalculateIngredientCustomizationPrice(rows, new[] { garlic.Id, mushrooms.Id }, null, 10)
            .Should().Be(2.00m);
    }

    [Fact]
    public void EqualPrices_TheWaiverFollowsDisplayOrder()
    {
        // Same price, so the tie-break decides — and the guest sheet draws its "Included" badge on
        // the first row it renders, which is DisplayOrder order. If this test goes red the badge
        // and the money have stopped agreeing.
        var first = Row("Ketchup", 0.60m, 0, IngredientKind.Sauce);
        var second = Row("Mustard", 0.60m, 1, IngredientKind.Sauce);
        var third = Row("Mayo", 0.60m, 2, IngredientKind.Sauce);
        var rows = new[] { third, first, second }; // deliberately not in DisplayOrder order

        var sut = CreateSut();

        // One free of three identical sauces: two are still charged whichever one is waived, so the
        // TOTAL cannot detect the tie-break. Drop the first row's price by a rappen and the
        // tie-break between the remaining two is what decides the answer.
        sut.CalculateIngredientCustomizationPrice(rows, new[] { first.Id, second.Id, third.Id }, null, 1)
            .Should().Be(1.20m);

        // Now make it observable: waive one of {Mustard, Mayo} while Ketchup is dearer and takes the
        // first free slot. With two free slots the second must go to Mustard (DisplayOrder 1), not
        // Mayo (DisplayOrder 2) — so Mayo is the row still paid for.
        var ketchup = Row("Ketchup", 0.90m, 0, IngredientKind.Sauce);
        var mustard = Row("Mustard", 0.60m, 1, IngredientKind.Sauce);
        var mayo = Row("Mayo", 0.60m, 2, IngredientKind.Sauce);
        sut.CalculateIngredientCustomizationPrice(
                new[] { mayo, ketchup, mustard }, new[] { ketchup.Id, mustard.Id, mayo.Id }, null, 2)
            .Should().Be(0.60m, "the free slots go to Ketchup then Mustard, leaving Mayo paid for");
    }

    [Fact]
    public void DeselectedSauces_WithAFreeAllowance_StillOnlyRefundWhatTheRuleRefunds()
    {
        // Two sauces the base price already includes, both taken OFF. Today's rule credits both
        // (that is the deduction guests get for stripping the dish), and the allowance must add
        // nothing on top: it can only remove a charge, and no charge was made.
        var mayo = Row("Mayo", 0.80m, 0, IngredientKind.Sauce, includedInBase: true, maxQuantity: 2);
        var garlic = Row("Garlic", 0.50m, 1, IngredientKind.Sauce, includedInBase: true, maxQuantity: 2);
        var rows = new[] { mayo, garlic };
        var empty = Array.Empty<Guid>();

        var withoutAllowance = CreateSut().CalculateIngredientCustomizationPrice(rows, empty, null, 0);
        withoutAllowance.Should().Be(-1.30m);

        CreateSut().CalculateIngredientCustomizationPrice(rows, empty, null, 2)
            .Should().Be(withoutAllowance, "a waiver removes charges; there are none to remove");
        CreateSut().CalculateIngredientCustomizationPrice(rows, empty, null, 5)
            .Should().Be(withoutAllowance);
    }

    [Fact]
    public void IncludedInBasePriceSauce_CountsOnlyItsExtraUnitsAsChargeable()
    {
        // Mayo is in the base price for one unit. The guest takes three. Today's rule charges for
        // two of them (0.80 x 2 = 1.60), so exactly two units are waivable — never three.
        var mayo = Row("Mayo", 0.80m, 0, IngredientKind.Sauce, includedInBase: true, maxQuantity: 3);
        var rows = new[] { mayo };
        var ids = new[] { mayo.Id };
        var three = new Dictionary<Guid, int> { [mayo.Id] = 3 };
        var sut = CreateSut();

        sut.CalculateIngredientCustomizationPrice(rows, ids, three, 0).Should().Be(1.60m);
        sut.CalculateIngredientCustomizationPrice(rows, ids, three, 1).Should().Be(0.80m);
        sut.CalculateIngredientCustomizationPrice(rows, ids, three, 2).Should().Be(0m);
        sut.CalculateIngredientCustomizationPrice(rows, ids, three, 3)
            .Should().Be(0m, "the free unit was never charged for, so it cannot be waived twice");

        // At quantity 1 the row is fully inside the base price: nothing is charged, nothing waived.
        var one = new Dictionary<Guid, int> { [mayo.Id] = 1 };
        sut.CalculateIngredientCustomizationPrice(rows, ids, one, 2).Should().Be(0m);
    }

    [Fact]
    public void AZeroPricedOrInactiveOrNonOptionalSauce_IsNeverAWaivableUnit()
    {
        // Three rows the loop must not treat as chargeable sauce units, each for a different reason.
        var free = Row("Free Sauce", 0m, 0, IngredientKind.Sauce);
        var inactive = Row("Discontinued Sauce", 1.00m, 1, IngredientKind.Sauce, isActive: false);
        var mandatory = Row("House Sauce", 1.00m, 2, IngredientKind.Sauce, isOptional: false);
        var paid = Row("Chilli", 1.20m, 3, IngredientKind.Sauce);
        var rows = new[] { free, inactive, mandatory, paid };
        var ids = new[] { free.Id, inactive.Id, mandatory.Id, paid.Id };

        // Today's price is 1.20 (only the paid, optional, active row bills). One free sauce must
        // spend itself on THAT row, not be swallowed by a 0.00 or a row the filter excludes.
        CreateSut().CalculateIngredientCustomizationPrice(rows, ids, null, 0).Should().Be(1.20m);
        CreateSut().CalculateIngredientCustomizationPrice(rows, ids, null, 1).Should().Be(0m);
    }

    [Fact]
    public void NonSauceIngredients_AreNeverWaived_HoweverExpensive()
    {
        var truffle = Row("Truffle Shavings", 9.00m, 0); // Kind = Ingredient
        var garlic = Row("Garlic Sauce", 0.50m, 1, IngredientKind.Sauce);
        var rows = new[] { truffle, garlic };

        // The dearest row on the line is not a sauce, so the free slot goes to the 0.50 sauce.
        CreateSut()
            .CalculateIngredientCustomizationPrice(rows, new[] { truffle.Id, garlic.Id }, null, 1)
            .Should().Be(9.00m);
    }

    [Fact]
    public void TheWaiverIgnoresTheOrderOfTheClientSuppliedSelection()
    {
        // The security property stated on its own: the price of a basket line must not be a
        // function of how the client happened to order its JSON array.
        var a = Row("Aioli", 1.10m, 0, IngredientKind.Sauce);
        var b = Row("BBQ", 2.30m, 1, IngredientKind.Sauce);
        var c = Row("Chilli", 0.70m, 2, IngredientKind.Sauce);
        var rows = new[] { a, b, c };
        var sut = CreateSut();

        foreach (var permutation in Permutations(new[] { a.Id, b.Id, c.Id }))
        {
            sut.CalculateIngredientCustomizationPrice(rows, permutation, null, 1)
                .Should().Be(1.80m, "BBQ is the dearest charged sauce in every ordering");
        }
    }

    // ---------------------------------------------------------------------------------------
    // The property test: 16 selections x 3 quantities x 4 allowances, against the reference rule.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void SauceWaiver_MatchesTheReferenceRule_ForEverySelectionAndQuantity()
    {
        // Four sauces chosen to cover the axes that matter: two plain paid rows at different
        // prices, one that ties with another on price (so the DisplayOrder tie-break is exercised),
        // and one the base price already includes (so "only extra units are chargeable" is too).
        var garlic = Row("Garlic", 0.50m, 0, IngredientKind.Sauce, maxQuantity: 3);
        var chilli = Row("Chilli", 1.20m, 1, IngredientKind.Sauce, maxQuantity: 3);
        var aioli = Row("Aioli", 1.20m, 2, IngredientKind.Sauce, maxQuantity: 3);
        var mayo = Row("Mayo", 0.90m, 3, IngredientKind.Sauce, includedInBase: true, maxQuantity: 3);

        // Two non-sauce rows travel along, because the rule has to leave them completely alone.
        var cheese = Row("Cheese", 1.50m, 4, includedInBase: true, maxQuantity: 3);
        var mushrooms = Row("Mushrooms", 2.00m, 5, maxQuantity: 3);

        var sauces = new[] { garlic, chilli, aioli, mayo };
        var rows = sauces.Concat(new[] { cheese, mushrooms }).ToArray();
        var sut = CreateSut();
        var cases = 0;

        foreach (var selection in Subsets(sauces))
        {
            // Cheese and mushrooms are always selected, so their money is a constant background the
            // waiver must never touch.
            var ids = selection.Select(r => r.Id).Concat(new[] { cheese.Id, mushrooms.Id }).ToList();

            for (var quantity = 1; quantity <= 3; quantity++)
            {
                var quantities = rows.ToDictionary(r => r.Id, _ => quantity);

                // 0 = today, 1..3 = allowances up to and beyond what the selection can spend.
                for (var includedFree = 0; includedFree <= 3; includedFree++)
                {
                    var expected = ReferenceRule(rows, ids, quantities, includedFree);
                    sut.CalculateIngredientCustomizationPrice(rows, ids, quantities, includedFree)
                        .Should().Be(expected,
                            "selection [{0}] at quantity {1} with {2} free",
                            string.Join(", ", selection.Select(r => r.Name)), quantity, includedFree);

                    // The two invariants that make this rule safe to ship, asserted on every case.
                    var today = PriceAsItWasBeforeS6(rows, ids, quantities);
                    expected.Should().BeLessThanOrEqualTo(today, "a waiver never ADDS money");
                    expected.Should().BeGreaterThanOrEqualTo(
                        today - sauces.Sum(s => s.Price * s.MaxQuantity),
                        "a waiver never gives back more than the sauces could possibly have cost");
                    cases++;
                }
            }
        }

        cases.Should().Be(16 * 3 * 4);
    }

    private static IEnumerable<ProductIngredient[]> Subsets(ProductIngredient[] rows)
    {
        for (var mask = 0; mask < (1 << rows.Length); mask++)
        {
            var subset = new List<ProductIngredient>();
            for (var bit = 0; bit < rows.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    subset.Add(rows[bit]);
                }
            }
            yield return subset.ToArray();
        }
    }

    private static IEnumerable<List<Guid>> Permutations(IReadOnlyList<Guid> ids)
    {
        if (ids.Count <= 1)
        {
            yield return ids.ToList();
            yield break;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            var rest = ids.Where((_, index) => index != i).ToList();
            foreach (var tail in Permutations(rest))
            {
                tail.Insert(0, ids[i]);
                yield return tail;
            }
        }
    }
}
