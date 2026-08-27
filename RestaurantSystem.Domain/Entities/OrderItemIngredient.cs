using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One rendered ingredient line of an <see cref="OrderItem"/>, FROZEN at checkout: the word the
/// guest was shown, the quantity they chose, and whether that counted as a removal.
/// <para>
/// It exists because the ingredient half of an order line was the only part of it that was NOT a
/// snapshot. <see cref="OrderItem.ProductName"/>, <see cref="OrderItem.VariationName"/>,
/// <see cref="OrderItem.UnitPrice"/> and <see cref="OrderItem.ItemTotal"/> are frozen columns, while
/// <see cref="OrderItem.IngredientQuantitiesJson"/> holds bare ids that were re-resolved against the
/// LIVE catalog on every read — so renaming or deleting a catalog row rewrote a receipt that had
/// already been printed. The owner settled it on 2026-08-24: a past receipt never changes
/// (SHARED-MODIFIERS-AND-SAUCES-PLAN D2, slice S1).
/// </para>
/// <para>
/// <b>These rows are written once and never updated.</b> Nothing in the codebase may recompute them,
/// and there is deliberately no price column here — see <c>OrderIngredientSnapshot</c> for why
/// recording money on this table would create a second price authority.
/// </para>
/// </summary>
public class OrderItemIngredient : Entity
{
    public Guid OrderItemId { get; set; }

    /// <summary>
    /// The <see cref="ProductIngredient"/> this row was projected from. PROVENANCE ONLY, and
    /// deliberately NOT a foreign key: the catalog row it names may be edited away or deleted, which
    /// is the very drift this table exists to survive. Readers must never resolve it.
    /// </summary>
    public Guid IngredientId { get; set; }

    /// <summary>
    /// The per-product name as it read at checkout (<c>ProductIngredient.Name</c>).
    /// <para>
    /// <c>required</c> rather than the <c>= null!</c> the older entities use: this column is the
    /// whole point of the table, so a construction site that forgets it should not compile. It
    /// follows <c>BaseEntity.CreatedBy</c>, which is already <c>required</c> on every entity here.
    /// </para>
    /// </summary>
    public required string IngredientName { get; set; }

    public int Quantity { get; set; }

    /// <summary>Frozen answer of <c>IngredientRecipeRules.IsRemoved</c> at checkout.</summary>
    public bool IsRemoved { get; set; }

    /// <summary>
    /// Position in the rendered list. The pre-snapshot read path iterated the product's recipe
    /// collection, so its order was the recipe's; freezing the index is what keeps a re-rendered
    /// historic line byte-identical rather than merely equivalent as a set.
    /// </summary>
    public int SortOrder { get; set; }

    // Navigation properties. Nullable, and honestly so: nothing loads it (the snapshot is read
    // through OrderItem.IngredientSnapshots, never the other way round), so declaring it non-null
    // would be a promise the runtime does not keep.
    public virtual OrderItem? OrderItem { get; set; }
}
