using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Services;

/// <summary>
/// The two things a bulk attach does to ONE product: decide whether it may, and then do it
/// (plan S8). Separated from the handler because the handler is at the file-length limit and
/// because both halves are worth reading on their own.
/// </summary>
internal static class GlobalIngredientAttach
{
    /// <summary>
    /// Would the product still satisfy backend #432's guard once this row is on it?
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate is the PRODUCT validator's own, reached by projecting the entities to
    /// <see cref="ProductIngredientDto"/>, so the bulk path and the product PUT cannot drift.
    /// <see cref="IncludedInBaseDeductionRule"/> exists because
    /// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> DEDUCTS the price of every
    /// optional, included-in-base, active ingredient the caller does not select — so a product whose
    /// included-in-base ingredients cost more than the product prices a NEGATIVE line, on request,
    /// from anyone. A bulk endpoint that skipped it would reopen that at 40x the scale.
    /// </para>
    /// <para>
    /// It is evaluated with the PRODUCT's own base price, hide-base flag and active variation
    /// modifiers, because the ceiling is per product while a bulk body carries one price for all of
    /// them. Reachable only when the body sets <c>isIncludedInBasePrice</c>; with the default it can
    /// never fire, which is why the check is cheap and still not optional.
    /// </para>
    /// </remarks>
    public static bool Fits(Product product, AttachGlobalIngredientDto body)
    {
        var ingredients = product.DetailedIngredients
            .Select(i => new ProductIngredientDto
            {
                Name = i.Name,
                IsOptional = i.IsOptional,
                IsIncludedInBasePrice = i.IsIncludedInBasePrice,
                IsActive = i.IsActive,
                Price = i.Price,
            })
            .Append(new ProductIngredientDto
            {
                Name = string.Empty,
                IsOptional = body.IsOptional,
                IsIncludedInBasePrice = body.IsIncludedInBasePrice,
                IsActive = true,
                Price = body.Price,
            });

        return IncludedInBaseDeductionRule.Fits(
            IncludedInBaseDeductionRule.MaxDeduction(ingredients),
            IncludedInBaseDeductionRule.MinEffectiveUnitPrice(
                product.BasePrice,
                product.HideBaseProduct,
                product.Variations.Where(v => v.IsActive).Select(v => v.PriceModifier)));
    }

    /// <summary>
    /// Copies the library row onto the product: the name, the kind, the nine translations, and the
    /// provenance link (plan D3). The four per-product facts come from the body, because plan D1
    /// says the PRODUCT row owns price, optionality and max quantity.
    /// </summary>
    public static void CopyOnto(
        ApplicationDbContext context,
        Product product,
        GlobalIngredient library,
        AttachGlobalIngredientDto body,
        string auditIdentifier)
    {
        var ingredient = new ProductIngredient
        {
            ProductId = product.Id,
            Name = library.DefaultName,
            Kind = library.Kind,
            GlobalIngredientId = library.Id,
            IsOptional = body.IsOptional,
            Price = body.Price,
            MaxQuantity = body.MaxQuantity,
            IsIncludedInBasePrice = body.IsIncludedInBasePrice,
            IsActive = true,
            // APPENDED across BOTH kinds, one past the highest position IN USE — never the row
            // count. See CatalogDisplayOrder: the column holds gaps and duplicates in live data, so
            // the count collides with an existing row and inserts into the middle of a recipe the
            // admin arranged by hand. Same rule as the picker's own append, on purpose.
            DisplayOrder = CatalogDisplayOrder.NextAfter(
                product.DetailedIngredients.Select(i => i.DisplayOrder)),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier,
        };

        context.ProductIngredients.Add(ingredient);

        // The translations are the whole value of a pick: the words repeat across a menu, and
        // retyping nine of them per product is the complaint this slice answers.
        foreach (var translation in library.Translations)
        {
            context.ProductIngredientDescriptions.Add(new ProductIngredientDescription
            {
                ProductIngredient = ingredient,
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
        + " the optional ingredients included in the base price would total more than the product "
        + "can be sold for, so an order that removed all of them would price below zero. "
        + "Deselect those products, or lower the price.";
}
