using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// How a CHILD order row is presented: which kind it is, and what its quantity means for the whole
/// line (#318). Extracted from <see cref="OrderMappingService"/> rather than inlined, because the
/// reasoning is longer than the code and that service is already over its §4 limit.
/// </summary>
internal static class OrderChildRendering
{
    /// <summary>
    /// The child's kind FOR DISPLAY. Byte-equivalent to the derivation this replaced, including its
    /// wrong answers, so no order changes the label it has always rendered.
    /// </summary>
    /// <remarks>
    /// A persisted <see cref="OrderItem.Kind"/> wins. Rows written before that column existed have
    /// null, and for those the old rule still applies: the PARENT's current product type. That rule
    /// is wrong — <c>Product.Type</c> is mutable, so retyping a product relabels the children of
    /// orders already placed (#318 item 3) — but it is what those orders have always rendered, and
    /// changing a historical label is not this fix's business.
    /// </remarks>
    internal static OrderItemKind DisplayKind(OrderItem child, OrderItem parent) =>
        child.Kind ?? (parent.Product?.Type == ProductType.Menu
            ? OrderItemKind.BundleChild
            : OrderItemKind.SideItem);

    /// <summary>
    /// A child's quantity for the WHOLE line, reconciling the two different things the stored value
    /// means: a side item's is PER UNIT of the parent line, a bundle option's is already
    /// line-absolute (<c>BuildMenuItemAsync</c> stores <c>item.Quantity * option.Quantity</c> and
    /// <c>BundleChildQuantityScaler</c> keeps it so — #305).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect: an order for 3 pizzas with a cola on the side printed "1 cola", while 2 bundles
    /// with a cola option printed "2". Both are child rows in the same collection and only one had
    /// been scaled.
    /// </para>
    /// <para>
    /// SCALING A BUNDLE CHILD WOULD BE THE BUG, not the fix — it is already line-absolute, so
    /// multiplying again gives quantity² behaviour.
    /// </para>
    /// <para>
    /// IT SCALES ONLY WHEN THE KIND IS KNOWN, and that is the whole reason this is not simply
    /// <c>DisplayKind(...) == SideItem</c>. <see cref="DisplayKind"/> resolves an UNRESOLVABLE parent
    /// to <c>SideItem</c>, because <c>parent.Product?.Type == ProductType.Menu</c> is false both when
    /// the parent is a plain product and when its navigation is null. That navigation really does go
    /// null: the global <c>IsDeleted</c> query filter applies to <c>Include</c>, so soft-deleting a
    /// bundle product empties it on every historical order that referenced it. Harmless while the
    /// answer only picked a label; fatal once it picks a multiplier — an adversarial review measured
    /// a stored 6 rendering as **18** on a 3-unit line, the exact quantity² outcome above, on every
    /// pre-existing order. So an unclassifiable row is left alone, mirroring #305's rule of skipping
    /// a row it cannot reason about rather than inventing a number.
    /// </para>
    /// <para>
    /// Computed in 64-bit and left unscaled if it would not fit an <c>int</c>. A line quantity is
    /// bounded 1..100 but a stored side quantity is bounded nowhere.
    /// </para>
    /// </remarks>
    internal static int LineQuantity(OrderItem child, OrderItem parent)
    {
        var isDefinitelySideItem = child.Kind == OrderItemKind.SideItem
            || (child.Kind is null && parent.Product is not null && parent.Product.Type != ProductType.Menu);

        if (!isDefinitelySideItem)
        {
            return child.Quantity;
        }

        var scaled = (long)child.Quantity * parent.Quantity;
        return scaled is >= int.MinValue and <= int.MaxValue ? (int)scaled : child.Quantity;
    }
}
