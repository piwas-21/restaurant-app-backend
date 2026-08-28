using System.Runtime.Serialization;

namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// What an ingredient row IS, on both the per-product row (<see cref="Entities.ProductIngredient"/>)
/// and the reusable library row (<see cref="Entities.GlobalIngredient"/>).
///
/// <para>
/// This is the whole of slice S5 of SHARED-MODIFIERS-AND-SAUCES-PLAN (D7/D8): a sauce is a TYPED
/// ingredient, not a second entity. The reason is decisive and is the only reason this shape was
/// chosen — <c>OrderItem.IngredientQuantitiesJson</c> and <c>BasketItem.IngredientQuantitiesJson</c>
/// are bare <c>Guid -&gt; int</c> maps with no table name in them. A second table would make every
/// one of those keys ambiguous across two tables, forcing a dual-read shim plus a migration of live
/// baskets AND of immutable order history. A discriminator column changes nothing about what an id
/// means, so cart, order, kitchen ticket, bundles and all nine display surfaces keep working
/// untouched, and the JSON columns are not written differently by one byte.
/// </para>
///
/// <para>
/// <b><see cref="Ingredient"/> is 0 on purpose.</b> The migration adds the column with a default of
/// 0, so every row that exists today keeps meaning exactly what it meant yesterday and no backfill
/// is needed. A client that never sends <c>kind</c> keeps creating ingredients.
/// </para>
///
/// <para>
/// This is NOT a general modifier-group engine. Adding a third member here buys grouping and
/// nothing else — there is still no min/max-select capability for arbitrary groups, which the owner
/// ruled out of scope twice (plan §7 Q2, reaffirmed 2026-08-27) and which stays a separate project.
/// </para>
/// </summary>
public enum IngredientKind
{
    /// <summary>An ordinary recipe/optional ingredient — the meaning every existing row has.</summary>
    [EnumMember(Value = "ingredient")]
    Ingredient = 0,

    /// <summary>A sauce. Same row, same controls, same money; grouped apart for the admin and (S6) the guest.</summary>
    [EnumMember(Value = "sauce")]
    Sauce = 1
}
