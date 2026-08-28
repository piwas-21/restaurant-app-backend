using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// What one <c>POST /api/orders</c> line said about its ingredients, resolved against the recipe:
/// the quantity map to persist, and — when the server was able to price the line itself — the
/// ingredient money to charge for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists (#430).</b> The waiter's take-order screen posts lines straight to this
/// endpoint. It could name a <c>customizationPrice</c>, but it could not say WHAT was customised:
/// the DTO carried only <c>IngredientQuantities</c>, a bare <c>Guid -&gt; int</c> map in which an
/// ingredient the caller never mentioned is indistinguishable from one the guest asked to have
/// taken off. So a waiter line reached the kitchen with its extras and removals surviving only as
/// prose in the note, and its frozen snapshot (S1) empty. <see cref="CreateOrderItemDto.SelectedIngredientIds"/>
/// is the missing half, and this class is what reads it.
/// </para>
/// <para>
/// <b>The rule: the server prices what it CAN price; only staff may declare what it cannot.</b>
/// A line that carries a selection is priced from the catalogue plus
/// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> over the POSTED selection,
/// and its declared <c>UnitPrice</c>/<c>CustomizationPrice</c> are ignored — on the staff path too.
/// Staff pricing is not a privilege to name any number; it is the carve-out for the lines the
/// catalogue cannot express (#329), and a line that has just described itself ingredient by
/// ingredient is not one of those.
/// </para>
/// <para>
/// <b>Two shapes keep the declared price, and both are cases where recomputing would be WRONG,
/// not merely unnecessary.</b> (i) A COMPOSED line — one with <c>ChildItems</c>, or a
/// <c>ProductType.Menu</c> bundle parent — has a price that lives in the menu definition and in the
/// rolled-up side-item total, neither of which <c>Product.BasePrice</c> can express; repricing it
/// here reproduces exactly the undercharge <c>OrderItemFactory</c>'s refusal guard exists to
/// prevent (measured there: 8.00 against a true 12.98), and would also make every side item free,
/// since a child row is pinned at <c>ItemTotal = 0</c>. The predicate is deliberately the SAME one
/// that guard uses, read from the already-loaded <c>Product.Type</c> so it costs no extra query.
/// (ii) A CHILD row: <c>BasketService</c> rolls a child's customization into the parent's price, so
/// adding a recomputed figure to the root total double-charges it (#150). Both still get their
/// selection turned into a quantity map — the map and the snapshot carry no money, so "NO onion"
/// reaches the kitchen either way.
/// </para>
/// <para>
/// <b>The guest checkout is untouched, structurally.</b> <c>BasketToOrderTranslator</c> never sets
/// <c>SelectedIngredientIds</c> — the basket has already settled its own money and re-pricing it
/// here would make a second authority out of a field. A null selection therefore means "resolved
/// exactly as before" for every caller that exists today, which is what keeps every anonymous
/// request byte-identical.
/// </para>
/// </remarks>
internal sealed record OrderLineIngredientChoice(Dictionary<Guid, int>? Quantities, decimal? Price)
{
    /// <param name="isRootLine">False for a bundle child or a side item — see the remarks.</param>
    internal static OrderLineIngredientChoice Resolve(
        ILineCustomizationBuilder builder,
        CreateOrderItemDto itemDto,
        Product product,
        bool isRootLine)
    {
        // Null, not Count == 0: an EMPTY list is a real answer ("every optional ingredient off"),
        // and it is the answer that costs the guest money, so it must not fall through to the
        // legacy branch that would leave the declared price standing.
        if (itemDto.SelectedIngredientIds is null)
        {
            return new OrderLineIngredientChoice(itemDto.IngredientQuantities, Price: null);
        }

        // preferProvidedQuantities: false — the bundle-child precedence, which BACKFILLS from the
        // selection even when a quantity map was also sent. That is the one that cannot be talked
        // out of writing an explicit 0 for a deselected ingredient, and the explicit 0 is what
        // OrderIngredientCustomizations turns into a "NO xxx" ticket line. The regular-item
        // precedence would persist the client's map verbatim, i.e. trust the caller to have zeroed
        // its own removals — the exact trust this change exists to withdraw.
        // sauceIncludedFree is passed EXPLICITLY, exactly as BasketItemFactory does
        // (BasketItemFactory.cs:51, :269). It defaults to 0, so omitting it compiles, prices a
        // sauce-allowance product as though it had none, and OVERCHARGES the guest for sauces the
        // dish includes (S6/#429, plan D10). A default that is silently wrong for this caller is
        // why it is named here rather than left off.
        var line = builder.Build(
            product.DetailedIngredients,
            itemDto.SelectedIngredientIds,
            itemDto.IngredientQuantities,
            preferProvidedQuantities: false,
            sauceIncludedFree: product.SauceIncludedFree);

        var serverCanPrice =
            isRootLine
            && itemDto.ChildItems is not { Count: > 0 }
            && product.Type != ProductType.Menu;

        return new OrderLineIngredientChoice(
            line.IngredientQuantities,
            serverCanPrice ? line.CustomizationPrice : null);
    }
}
