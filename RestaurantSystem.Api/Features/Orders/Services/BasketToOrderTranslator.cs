using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
/// <remarks>
/// Ported from the former frontend <c>utils/orderItemsPayload.ts</c> so the money-path transform is
/// owned by the backend, not the client (menu-bundles redesign #157, slice 5). The mapping was
/// byte-identical to that transform until #312, which corrected the one field the port carried over
/// wrong — see <see cref="LineAbsoluteCustomization"/>:
/// <list type="bullet">
/// <item>Top-level side items AND bundle children both become order child rows.</item>
/// <item>A child's <c>CustomizationPrice</c> is sent as 0 — <c>BasketService</c> already rolled
/// each child's customization price into the parent's <c>UnitPrice</c>, so a non-zero value here
/// would be double-counted into the root <c>ItemTotal</c> by <c>OrderItemFactory</c> (issue #150).</item>
/// <item>Deselected ingredients are zeroed (not dropped) so <c>OrderMappingService</c> can derive
/// <c>IsRemoved</c> for the kitchen ticket.</item>
/// </list>
/// </remarks>
public class BasketToOrderTranslator : IBasketToOrderTranslator
{
    public List<CreateOrderItemDto> Translate(IEnumerable<BasketItemDto> basketItems) =>
        basketItems.Select(MapTopLevelItem).ToList();

    private static CreateOrderItemDto MapTopLevelItem(BasketItemDto item)
    {
        var orderItem = new CreateOrderItemDto
        {
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            MenuId = item.MenuId,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            CustomizationPrice = LineAbsoluteCustomization(item),
            SpecialInstructions = item.SpecialInstructions,
            IngredientQuantities = BuildIngredientQuantities(item),
        };

        var childItems = new List<CreateOrderItemDto>();

        // Top-level side items → child rows (pre-existing behaviour, unchanged).
        //
        // Quantity stays PER UNIT, deliberately: it is what the client sent and what
        // BasketMappingService serves, so the basket, the cart and the order agree. #318's fix is in
        // the RENDERER, not here — scaling it at this point would make the order disagree with the
        // basket the guest actually saw. Kind is what lets the renderer tell it apart from a bundle
        // child, whose quantity is already line-absolute.
        if (item.SelectedSideItems is { Count: > 0 })
        {
            childItems.AddRange(item.SelectedSideItems.Select(side => new CreateOrderItemDto
            {
                ProductId = side.Id,
                Quantity = side.Quantity,
                UnitPrice = side.Price,
                CustomizationPrice = 0m,
                Kind = OrderItemKind.SideItem,
            }));
        }

        // Bundle children (menu options) with their per-option customizations (issue #150).
        if (item.ChildItems is { Count: > 0 })
        {
            childItems.AddRange(item.ChildItems.Select(MapBundleChild));
        }

        if (childItems.Count > 0)
        {
            orderItem.ChildItems = childItems;
        }

        return orderItem;
    }

    /// <summary>
    /// The root line's customization expressed the way <see cref="CreateOrderItemDto"/> declares it —
    /// the TOTAL FOR ALL QUANTITIES — derived from the basket line total rather than copied from
    /// <see cref="BasketItemDto.CustomizationPrice"/> (#312).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>OrderItemFactory</c> computes a root line as <c>(UnitPrice * Quantity) + CustomizationPrice</c>,
    /// which is correct against that documented contract. The BASKET stores the field two different
    /// ways, because <c>BasketItemFactory</c> builds the two root shapes differently (see
    /// <c>BasketLineTotal</c>): a regular row keeps customization OUT of <c>UnitPrice</c> and stores it
    /// PER UNIT, while a bundle row folds it INTO <c>UnitPrice</c> and keeps a copy for display. Copying
    /// the field therefore broke the contract in two OPPOSITE directions, both measured through
    /// <c>POST /api/orders/from-basket</c>: a regular line (qty 3, 12.99 plus a 2.99 side item) produced
    /// an order line of 41.96 against a basket 47.94 — 5.98 UNDER — and a customised bundle (qty 2, unit
    /// 16.00 with 3.00 of customization already inside it) produced 35.00 against a basket 32.00 — 3.00
    /// OVER, which is the double-charge #308 fixed one layer up.
    /// </para>
    /// <para>
    /// Multiplying by <c>Quantity</c> is the obvious repair and is wrong: it corrects the regular line
    /// (8.97) and takes the bundle to 38.00 where 32.00 is right. Subtracting needs no discrimination
    /// between the two shapes and does not have to guess which one a row is —
    /// <c>BasketLineTotal.ForRoot</c> has already resolved that when the row was written. It is an
    /// identity, not an estimate: <c>U*Q + (ItemTotal - U*Q) == ItemTotal</c> holds for ANY stored
    /// total, so no row shape can break it. A NEGATIVE result is legitimate rather than something to
    /// clamp: removing a priced included-in-base ingredient is a genuine deduction (#304).
    /// </para>
    /// <para>
    /// THE PRECONDITION on "the order line equals the basket line", which is NOT unconditional:
    /// <c>OrderItemFactory.ResolvePricing</c> echoes the DTO's <c>UnitPrice</c> only while it is
    /// <c>&gt; 0</c>. At or below zero it re-prices from <c>product.BasePrice</c> READ AT CHECKOUT, and
    /// pairs that with a customization derived here against the basket's <c>UnitPrice</c> of 0. So a
    /// zero-priced product whose price an admin edits while the line sits in a live basket still
    /// diverges — pre-existing in kind (the old copy diverged there too, by a different amount), and
    /// left alone deliberately: <c>UnitPrice &lt;= 0</c> means "server, you price it" for a caller that
    /// hand-builds <c>POST /api/orders</c>, which is a feature of that path rather than a bug.
    /// </para>
    /// <para>
    /// The invariant the tests assert — <c>sum(order.Items.ItemTotal) == basket.SubTotal</c> — leans on
    /// one more thing worth naming, because it is easy to state wrongly:
    /// <c>BasketPricingService.ApplyTotalsAsync</c> sums EVERY basket item, root and child alike. It
    /// equals the sum of the ROOT totals only because a child is pinned at <c>ItemTotal = 0</c>
    /// (<c>BasketItemFactory</c>, preserved by <c>BundleChildQuantityScaler</c>) — and the order side
    /// mirrors that pin (#54). Break the children-carry-zero convention on either side and this
    /// reconciliation goes with it.
    /// </para>
    /// <para>
    /// Scope: the BASKET producer only. A caller that hand-builds <c>POST /api/orders</c> still owns the
    /// contract itself, and <c>OrderItemFactory</c> is unchanged — including its legacy <c>MenuId</c>
    /// branch, which prices from <c>Menus.BasePrice</c> instead of the DTO's <c>UnitPrice</c> and would
    /// not pair with a value derived here. AS OF THIS COMMIT the basket cannot reach it: no code path
    /// assigns <c>BasketItem.MenuId</c>, because <c>BasketService</c>'s <c>MenuId</c> branch is an empty
    /// block. That is a current-code fact and NOT a structural guarantee — <c>c5180c1</c> did populate
    /// that column, and the empty block still says "keep for backward compatibility if needed", so
    /// re-enabling it would silently route bundle lines into a branch that never recurses into
    /// <c>ChildItems</c> and drops every child row from the order. <c>OrderLineCustomizationPriceTests</c>
    /// pins <c>MenuId</c> null on the real producer chain so that change cannot land unnoticed. The
    /// branch is still reachable from the anonymous <c>POST /api/orders</c>, so it is NOT dead code —
    /// removal belongs to #160.
    /// </para>
    /// </remarks>
    private static decimal LineAbsoluteCustomization(BasketItemDto item) =>
        item.ItemTotal - (item.UnitPrice * item.Quantity);

    // A bundle-child basket item (a menu option chosen in the bundle modal). CustomizationPrice is
    // 0 (see class remarks); the child keeps its own instructions + ingredient customizations and
    // recurses for any nested children.
    private static CreateOrderItemDto MapBundleChild(BasketItemDto child)
    {
        var childItem = new CreateOrderItemDto
        {
            ProductId = child.ProductId,
            ProductVariationId = child.ProductVariationId,
            // Already LINE-ABSOLUTE when it was written: BuildMenuItemAsync stores
            // `item.Quantity * option.Quantity` and BundleChildQuantityScaler keeps it that way when
            // the parent's quantity moves (#305). The renderer must therefore NOT scale it again —
            // double-scaling a bundle child is the obvious way to get #318 wrong.
            Quantity = child.Quantity,
            UnitPrice = child.UnitPrice,
            CustomizationPrice = 0m,
            SpecialInstructions = child.SpecialInstructions,
            IngredientQuantities = BuildIngredientQuantities(child),
            Kind = OrderItemKind.BundleChild,
        };

        if (child.ChildItems is { Count: > 0 })
        {
            childItem.ChildItems = child.ChildItems.Select(MapBundleChild).ToList();
        }

        return childItem;
    }

    /// <summary>
    /// A copy of the item's ingredient-quantity map with every ingredient NOT in
    /// <see cref="BasketItemDto.SelectedIngredients"/> zeroed out — an explicit 0 is how
    /// <c>OrderMappingService</c> derives <c>IsRemoved</c> for the kitchen ticket. Returns
    /// <c>null</c> when the item carries no quantities (the field is then omitted), matching the
    /// former frontend behaviour.
    /// </summary>
    private static Dictionary<Guid, int>? BuildIngredientQuantities(BasketItemDto item)
    {
        if (item.IngredientQuantities is not { Count: > 0 })
        {
            return null;
        }

        var processed = new Dictionary<Guid, int>(item.IngredientQuantities);

        if (item.SelectedIngredients is not null)
        {
            var deselected = processed.Keys
                .Where(id => !item.SelectedIngredients.Contains(id))
                .ToList();
            foreach (var ingredientId in deselected)
            {
                processed[ingredientId] = 0;
            }
        }

        return processed;
    }
}
