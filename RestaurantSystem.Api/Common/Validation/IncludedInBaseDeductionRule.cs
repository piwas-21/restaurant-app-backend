using System.Globalization;
using FluentValidation;
using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// The catalogue guard for the included-in-base deduction, shared by the product create and update
/// commands (backend #432).
///
/// <para>
/// <b>What it prevents.</b> <c>BasketPricingService.CalculateIngredientCustomizationPrice</c>
/// DEDUCTS <c>Price</c> for every ingredient that is optional, included in the base price, active,
/// and NOT in the posted selection (#304) — and #430 made an empty selection reachable by an
/// anonymous <c>POST /api/orders</c>. So a product whose included-in-base ingredients cost MORE than
/// the product itself prices a NEGATIVE order line, on request, from anyone.
/// </para>
///
/// <para>
/// <b>Why here and not in the money path.</b> #430 declined a per-line floor for four reasons that
/// this rule does not overturn: a negative line is a documented legitimate result in
/// <c>BasketToOrderTranslator</c>; a floor breaks the <c>sum(Items.ItemTotal) == basket.SubTotal</c>
/// reconciliation; <c>OrderPricingService</c> already floors the ORDER total at 0; and the same lever
/// pre-exists through the basket path. The misconfiguration is BORN in the product editor, so it is
/// caught there — which also protects the basket path, as a floor in the order path would not.
/// </para>
///
/// <para>
/// <b>Strictly greater, and that is a measured decision.</b> On the live RUMI catalogue (measured
/// 2026-08-28, <c>docs/plans/_research/included-in-base-deduction-exposure.md</c>) one of 77 products
/// sits EXACTLY on the boundary: <c>Lebanese Plate</c> costs 22.90 and is built as the exact sum of
/// its seven mezze, 22.90 to the cent. Equality prices the line at 0.00 — not below — and is also
/// what the configuration MEANS: the plate is its mezze, so removing all of them leaves an empty
/// plate. A <c>&gt;=</c> rule would therefore refuse a product that is live and correct today, which
/// is a regression, not a guard. <c>&gt;</c> refuses exactly the states that produce a negative line
/// and no others; today zero live products fail it.
/// </para>
/// </summary>
public static class IncludedInBaseDeductionRule
{
    /// <summary>
    /// The most the customization price can ever be REDUCED by, which is the deselect-everything
    /// case. Mirrors the deducting branch of
    /// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> exactly: the three flags it
    /// tests, and the included quantity of ONE (<c>MaxQuantity</c> only ever adds). The sauce
    /// allowance cannot widen it — that waiver only ever removes a charge this same loop added, and
    /// an unselected row is charged nothing.
    /// </summary>
    public static decimal MaxDeduction(IEnumerable<ProductIngredientDto>? ingredients) =>
        ingredients?
            .Where(i => i.IsOptional && i.IsIncludedInBasePrice && i.IsActive)
            .Sum(i => i.Price) ?? 0m;

    /// <summary>
    /// The cheapest unit price a line can actually be sold at, because that is what the deduction
    /// eats into: <c>ResolvePricing</c> returns <c>BasePrice + PriceModifier</c> and a modifier may
    /// be NEGATIVE. When <paramref name="hideBaseProduct"/> is set the bare base row is not
    /// orderable, so only the variations count — unless there are none, in which case the product
    /// falls back to its base price rather than to nothing.
    /// </summary>
    public static decimal MinEffectiveUnitPrice(
        decimal basePrice,
        bool hideBaseProduct,
        IEnumerable<decimal>? activePriceModifiers)
    {
        var modifiers = activePriceModifiers?.ToList() ?? [];
        if (modifiers.Count == 0)
        {
            return basePrice;
        }

        var cheapestVariation = basePrice + modifiers.Min();
        return hideBaseProduct ? cheapestVariation : Math.Min(basePrice, cheapestVariation);
    }

    /// <summary>The one predicate, so the validator and the message cannot disagree.</summary>
    public static bool Fits(decimal maxDeduction, decimal minEffectiveUnitPrice) =>
        maxDeduction <= minEffectiveUnitPrice;

    /// <summary>
    /// Names both numbers and the shortfall, because the admin's fix is one of two specific edits and
    /// a bare "invalid product" would not say which.
    /// </summary>
    public static string BuildMessage(decimal maxDeduction, decimal minEffectiveUnitPrice) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "The optional ingredients included in the base price total {0:0.00}, which is more than "
            + "the {1:0.00} this product can be sold for. An order that removes all of them would "
            + "price below zero. Raise the price to at least {0:0.00}, or lower an included "
            + "ingredient by {2:0.00}.",
            maxDeduction,
            minEffectiveUnitPrice,
            maxDeduction - minEffectiveUnitPrice);

    /// <summary>
    /// Applies the rule to a command carrying a base price, the hide-base flag, the detailed
    /// ingredients, and the price modifiers of its ACTIVE variations.
    /// </summary>
    /// <remarks>
    /// The variations arrive already projected to their modifiers because the create and update
    /// payloads are separate records with separate variation types — the same reason
    /// <see cref="ProductVariationRule"/> takes accessors. It is attached to the ingredients
    /// property so the 400 points at the collection the admin has to edit.
    /// </remarks>
    public static void ValidateIncludedInBaseDeduction<T>(
        this AbstractValidator<T> validator,
        Func<T, decimal> basePrice,
        Func<T, bool> hideBaseProduct,
        Func<T, IEnumerable<ProductIngredientDto>?> detailedIngredients,
        Func<T, IEnumerable<decimal>?> activePriceModifiers)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(basePrice);
        ArgumentNullException.ThrowIfNull(hideBaseProduct);
        ArgumentNullException.ThrowIfNull(detailedIngredients);
        ArgumentNullException.ThrowIfNull(activePriceModifiers);

        validator.RuleFor(command => command)
            .Must(command => Fits(
                MaxDeduction(detailedIngredients(command)),
                MinEffectiveUnitPrice(basePrice(command), hideBaseProduct(command), activePriceModifiers(command))))
            .WithName("DetailedIngredients")
            .WithMessage(command => BuildMessage(
                MaxDeduction(detailedIngredients(command)),
                MinEffectiveUnitPrice(basePrice(command), hideBaseProduct(command), activePriceModifiers(command))));
    }
}
