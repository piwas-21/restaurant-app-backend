namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// What a CHILD <c>OrderItem</c> is: a component of a bundle/combo, or a true add-on side item.
/// Null on a top-level line, and null on every row written before this column existed.
/// </summary>
/// <remarks>
/// <para>
/// This used to be derived at render time from the PARENT's <c>Product.Type</c>
/// (<c>OrderMappingService</c>, menu-bundles redesign #158). That is not a property of the child row
/// at all, and it is mutable: <c>UpdateProductCommand</c> assigns <c>product.Type</c> unguarded, so
/// retyping a product silently relabelled the children of orders ALREADY PLACED, and a line holding
/// both kinds labelled both the same way (#318). <c>BasketLineTotal</c> refuses to key on the same
/// fact for the same reason.
/// </para>
/// <para>
/// It is recorded at write time instead, by the producer that knows: <c>BasketToOrderTranslator</c>'s
/// two <c>AddRange</c> calls each build exactly one kind. The alternative — <c>OrderItem.MenuId</c> —
/// is NOT a discriminator: nothing assigns <c>BasketItem.MenuId</c> (<c>BasketService</c>'s branch on
/// it is an empty block), so it is NULL on every row the basket has ever written, and a bundle is a
/// <c>Product</c> with <c>Type = ProductType.Menu</c> reached by <c>ProductId</c>.
/// </para>
/// <para>
/// BOTH the member ORDER and the member NAMES are load-bearing, for different reasons, and it is
/// worth being exact because the two go opposite ways. EF stores the enum as an <c>integer</c>
/// column, so the ORDER decides what is written to the database. The API registers a
/// <c>StringEnumConverterFactory</c> (Program.cs) whose factory explicitly handles
/// <c>Nullable&lt;TEnum&gt;</c>, so the wire carries the NAME — <c>"SideItem"</c>, which
/// <c>lineSummary.ts</c> filters on by string equality in the frontend.
/// </para>
/// <para>
/// This enum replaced an API-layer <c>ItemKind</c> with the same two members in the same order, so
/// the stored value and the wire value are both unchanged by the move. Do not reorder, rename, or
/// insert members.
/// </para>
/// </remarks>
public enum OrderItemKind
{
    BundleChild = 0,
    SideItem = 1
}
