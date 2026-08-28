using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Services;

/// <summary>
/// The two things a bulk variation attach does to ONE product: decide whether it may, and then do
/// it (plan S8). The ingredient twin is <c>GlobalIngredientAttach</c>; the two are separate because
/// what they copy and what they must check are genuinely different, not because the shape repeats.
/// </summary>
internal static class GlobalVariationAttach
{
    /// <summary>
    /// Would the product still satisfy backend #432's guard once this variation is on it?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not the ingredient check with the words changed — a variation reaches that rule
    /// from the OTHER side.</b> <c>IncludedInBaseDeductionRule</c> compares the most a line can be
    /// DEDUCTED by against <c>MinEffectiveUnitPrice</c>, and a price modifier may be NEGATIVE, so a
    /// variation lowers the number the deduction eats into without touching the deduction itself. A
    /// product selling at 10.00 with 8.00 of removable included-in-base value is fine until a
    /// "Small −3.00" arrives; the cheapest sellable unit becomes 7.00 and an order that deselects
    /// everything prices the line at −1.00. So attaching a DISCOUNT to forty products is a money
    /// defect the ingredient path cannot produce, and the guard has to run here too.
    /// </para>
    /// <para>
    /// <c>HideBaseProduct</c> matters for the same reason it does in the validator: when the bare
    /// base row is not orderable, only the variations count, so a negative modifier is the whole
    /// price rather than an alternative to it.
    /// </para>
    /// </remarks>
    public static bool Fits(Product product, AttachGlobalVariationDto body)
    {
        var ingredients = product.DetailedIngredients.Select(i => new ProductIngredientDto
        {
            Name = i.Name,
            IsOptional = i.IsOptional,
            IsIncludedInBasePrice = i.IsIncludedInBasePrice,
            IsActive = i.IsActive,
            Price = i.Price,
        });

        var modifiers = product.Variations
            .Where(v => v.IsActive)
            .Select(v => v.PriceModifier)
            .Append(body.PriceModifier);

        return IncludedInBaseDeductionRule.Fits(
            IncludedInBaseDeductionRule.MaxDeduction(ingredients),
            IncludedInBaseDeductionRule.MinEffectiveUnitPrice(
                product.BasePrice, product.HideBaseProduct, modifiers));
    }

    /// <summary>
    /// Copies the library row onto the product: the name, the translations, and the provenance link
    /// (plan D3). The price modifier comes from the body, because that is the fact the library could
    /// never have known.
    /// </summary>
    public static void CopyOnto(
        ApplicationDbContext context,
        Product product,
        GlobalVariation library,
        AttachGlobalVariationDto body,
        string auditIdentifier)
    {
        var variation = new ProductVariation
        {
            ProductId = product.Id,
            Name = library.DefaultName,
            GlobalVariationId = library.Id,
            PriceModifier = body.PriceModifier,
            IsActive = true,
            // One past the highest position IN USE, never the row count — see CatalogDisplayOrder,
            // and the same rule the picker's own append uses (nextVariationDisplayOrder).
            DisplayOrder = CatalogDisplayOrder.NextAfter(product.Variations.Select(v => v.DisplayOrder)),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier,
        };

        context.ProductVariations.Add(variation);

        foreach (var translation in library.Translations)
        {
            context.ProductVariationDescriptions.Add(new ProductVariationDescription
            {
                ProductVariation = variation,
                LanguageCode = translation.LanguageCode,
                Name = translation.Name,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier,
            });
        }
    }

    /// <summary>
    /// Names every product that blocked the batch, because the admin's fix is per product and
    /// "invalid request" would not say which of forty to look at.
    /// </summary>
    public static string BuildRefusalMessage(IEnumerable<string> refusedProductNames) =>
        "Nothing was attached. On "
        + string.Join(", ", refusedProductNames)
        + " this price modifier would take the cheapest sellable price below the optional "
        + "ingredients that are included in the base price, so an order that removed all of them "
        + "would price below zero. Deselect those products, or raise the modifier.";
}
