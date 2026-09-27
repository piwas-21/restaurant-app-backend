using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The typed ingredient-customization state for one basket line: the persisted columns plus the
/// ingredient customization price. One writer for both the regular-item and bundle-child paths
/// (menu-bundles redesign #155), so a new customization field can never again be added to one
/// branch and forgotten in the other.
/// </summary>
public sealed record LineCustomization(
    List<Guid>? SelectedIngredients,
    string? IngredientQuantitiesJson,
    decimal CustomizationPrice)
{
    /// <summary>
    /// The very map <see cref="IngredientQuantitiesJson"/> is the serialization of — null exactly
    /// when that string is. Additive (an init-only member, not a positional one), so no existing
    /// call site moves.
    /// <para>
    /// It exists for a consumer that needs the map TYPED rather than as JSON: the order path builds
    /// its frozen ingredient snapshot from a <c>Dictionary&lt;Guid,int&gt;</c>
    /// (<c>OrderIngredientSnapshot.Build</c>), and the alternative was to deserialize back the
    /// string this builder had just written. Handing back both views keeps ONE writer of the rule
    /// "which ingredient ends up at which quantity", which is the whole point of this class.
    /// </para>
    /// <para>
    /// <b>It may be the CALLER'S OWN instance — do not mutate it.</b> Two precedence branches pass
    /// the provided map straight through rather than copying it, and <see cref="SelectedIngredients"/>
    /// has always aliased the caller's list the same way. Every consumer today only reads.
    /// </para>
    /// </summary>
    public Dictionary<Guid, int>? IngredientQuantities { get; init; }
}

public sealed record LineCustomizationOptions(
    int SauceIncludedFree = 0,
    int? SauceMax = null,
    ICollection<ProductCustomizationGroup>? ExplicitGroups = null,
    int SauceMin = 0)
{
    public static LineCustomizationOptions FromProduct(Product product) => new(
        product.SauceIncludedFree,
        product.SauceMax,
        product.CustomizationGroups,
        product.SauceMin);
}

public interface ILineCustomizationBuilder
{
    /// <param name="preferProvidedQuantities">
    /// Selects the (historically divergent, behaviour-preserved) quantity-JSON precedence:
    /// <c>true</c> = the regular-item rule (an explicit client map is persisted verbatim, else
    /// backfill from the selection); <c>false</c> = the bundle-child rule (backfill from the
    /// selection when present, else persist a provided map as-is). Since #303 the two agree on the
    /// one thing that is not precedence: neither backfills without a selection to backfill FROM.
    /// </param>
    /// <param name="options">
    /// The product-specific sauce and explicit-group rules. The caller supplies these because it
    /// holds the product row; omitted options retain the historical zero/default behavior.
    /// </param>
    LineCustomization Build(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities,
        LineCustomizationOptions? options = null);

    /// <summary>Checks minimum sauces for a line after deserializing its legacy quantity map.</summary>
    void EnsureAtLeastMinimum(BasketItem line);

    /// <summary>Checks an incoming selection against the tenant's minimum-sauce policy.</summary>
    void EnsureAtLeastMinimum(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities,
        int sauceMin);
}

public class LineCustomizationBuilder : ILineCustomizationBuilder
{
    private readonly IBasketPricingService _basketPricingService;
    private readonly ITenantFeatures? _tenantFeatures;

    public LineCustomizationBuilder(
        IBasketPricingService basketPricingService,
        ITenantFeatures? tenantFeatures = null)
    {
        _basketPricingService = basketPricingService;
        _tenantFeatures = tenantFeatures;
    }

    public LineCustomization Build(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities,
        LineCustomizationOptions? options = null)
    {
        SauceSelectionRule.EnsureWithinMaximum(detailedIngredients, selectedIngredients, options?.SauceMax);
        EnsureAtLeastMinimum(
            detailedIngredients, selectedIngredients, ingredientQuantities, options?.SauceMin ?? 0);

        // A payload carrying neither a selection nor a quantity map expressed no ingredient choice
        // at all, so there is nothing to record and nothing to price (#303). `useReorder` posts
        // exactly that: product + quantity.
        //
        // The price half matters as much as the JSON half. BasketPricingService reads a null
        // selection as "everything deselected" and therefore DEDUCTS every optional ingredient that
        // is included in the base price — so a re-ordered pizza was billed as though the cheese had
        // been taken off it. That rule is correct for a caller that has a selection
        // (Customization_NullSelected_TreatsAllAsDeselected pins it deliberately); what was wrong
        // was asking it about a payload that never answered the question. Hence the gate lives
        // here, at the one call site, and not inside the pricing service.
        var expressedAnIngredientChoice = selectedIngredients != null || ingredientQuantities is { Count: > 0 };

        var customizationPrice = expressedAnIngredientChoice
            ? _basketPricingService.CalculateIngredientCustomizationPrice(
                detailedIngredients,
                selectedIngredients,
                ingredientQuantities,
                options?.SauceIncludedFree ?? 0,
                options?.ExplicitGroups)
            : 0m;

        var resolvedQuantities = ResolveIngredientQuantities(
            detailedIngredients, selectedIngredients, ingredientQuantities, preferProvidedQuantities);

        return new LineCustomization(
            selectedIngredients,
            resolvedQuantities != null ? JsonSerializer.Serialize(resolvedQuantities) : null,
            customizationPrice)
        {
            IngredientQuantities = resolvedQuantities,
        };
    }

    public void EnsureAtLeastMinimum(BasketItem line) =>
        SauceSelectionRule.EnsureAtLeastMinimum(
            line,
            _tenantFeatures?.EnforceSauceMinimum == true);

    public void EnsureAtLeastMinimum(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities,
        int sauceMin) =>
        SauceSelectionRule.EnsureAtLeastMinimum(
            detailedIngredients,
            selectedIngredientIds,
            ingredientQuantities,
            sauceMin,
            _tenantFeatures?.EnforceSauceMinimum == true);

    /// <summary>
    /// The precedence rule itself, returning the MAP. Serialization moved to the single call site
    /// above so the string and the dictionary cannot say different things.
    /// </summary>
    private static Dictionary<Guid, int>? ResolveIngredientQuantities(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities)
    {
        if (preferProvidedQuantities)
        {
            // Regular-item precedence (was BuildRegularItemAsync): an explicit client map is
            // persisted verbatim; only without one do we backfill from the selection.
            if (ingredientQuantities is { Count: > 0 })
            {
                return ingredientQuantities;
            }

            // The selection gate matches the bundle-child branch below, and closes issue #303.
            // Backfilling with a NULL selection writes an explicit 0 for every unselected active
            // ingredient that is optional or included in the base price (see BuildIngredientQuantities
            // — NOT the same set as IngredientRecipeRules' base recipe), which is indistinguishable
            // from a guest who stripped the dish bare. `useReorder` posts exactly that payload:
            // product + quantity, no selection, no quantities. Those zeroes reach the order via
            // BasketToOrderTranslator, and OrderMappingService reads each one that IS in the base
            // recipe as IsRemoved — so the kitchen ticket printed "NO Cheese" for a re-ordered
            // Margherita.
            // An EMPTY selection still backfills: that is a guest who deselected everything.
            if (selectedIngredients != null && detailedIngredients is { Count: > 0 })
            {
                var built = BuildIngredientQuantities(detailedIngredients, selectedIngredients, ingredientQuantities);
                return built.Count > 0 ? built : null;
            }

            return null;
        }

        // Bundle-child precedence (was the BuildMenuItemAsync child loop): backfill from the
        // selection when present (so a deselected optional's "NO xxx" always reaches the kitchen
        // ticket), otherwise persist a provided map as-is.
        if (selectedIngredients != null && detailedIngredients is { Count: > 0 })
        {
            var built = BuildIngredientQuantities(detailedIngredients, selectedIngredients, ingredientQuantities);
            return built.Count > 0 ? built : null;
        }

        if (ingredientQuantities is { Count: > 0 })
        {
            return ingredientQuantities;
        }

        return null;
    }

    /// <summary>
    /// Builds the per-ingredient quantity map persisted as <c>IngredientQuantitiesJson</c>.
    /// A client-provided quantity wins; otherwise a selected ingredient gets quantity 1 and a
    /// deselected optional / included-in-base ingredient gets an explicit quantity 0, so the
    /// kitchen ticket can print "NO xxx" (OrderMappingService derives IsRemoved from 0).
    /// Non-optional ingredients missing from the selection are implicitly included (no entry).
    /// <para>Both callers gate this on a non-null selection (#303): every 0 it writes is a
    /// statement that the guest chose to leave that ingredient out, so it must not run for a
    /// payload that expressed no choice.</para>
    /// </summary>
    private static Dictionary<Guid, int> BuildIngredientQuantities(
        IEnumerable<ProductIngredient> detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredients,
        Dictionary<Guid, int>? providedQuantities)
    {
        var selectedIngredientIds = selectedIngredients != null
            ? new HashSet<Guid>(selectedIngredients)
            : new HashSet<Guid>();
        var builtQuantities = new Dictionary<Guid, int>();

        foreach (var ingredient in detailedIngredients.Where(i => i.IsActive))
        {
            if (providedQuantities != null && providedQuantities.TryGetValue(ingredient.Id, out var quantity))
            {
                // Explicit client quantity (e.g. double cheese) takes precedence.
                builtQuantities[ingredient.Id] = quantity;
            }
            else if (selectedIngredientIds.Contains(ingredient.Id))
            {
                // Selected ingredient without an explicit quantity: quantity 1
                builtQuantities[ingredient.Id] = 1;
            }
            else if (ingredient.IsOptional || ingredient.IsIncludedInBasePrice)
            {
                // Optional ingredient not selected: mark as deselected (quantity 0)
                builtQuantities[ingredient.Id] = 0;
            }
            // Non-optional ingredients that are not selected are implicitly included
        }

        return builtQuantities;
    }
}
