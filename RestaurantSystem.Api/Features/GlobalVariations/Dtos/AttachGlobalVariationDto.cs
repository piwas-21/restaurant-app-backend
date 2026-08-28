namespace RestaurantSystem.Api.Features.GlobalVariations.Dtos;

/// <summary>
/// The body of <c>POST /api/global-variations/{id}/attach</c> — plan S8, "reuse at scale", the
/// variation half of <i>"why must I retype this on 40 pizzas"</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explicit product ids, never a category</b> — same reason as the ingredient body: "apply to
/// every pizza" is resolved by the CLIENT into the ids it is about to send, so the blast-radius
/// confirm (plan D6) and the payload are the same list by construction. A server-side category
/// target would be re-resolved at save time, and a product added to that category between the
/// confirm and the save would be changed by an action nobody saw.
/// </para>
/// <para>
/// <b>One field, and it is the price.</b> The catalog deliberately carries no money (backend #431):
/// "Large" is +2.00 on a pizza and +0.50 on a coffee, so the library holds the words and the product
/// holds the number. 0 is the neutral modifier and the default, which makes an attached row
/// immediately sellable at the base price rather than at a wrong one.
/// </para>
/// </remarks>
public class AttachGlobalVariationDto
{
    public List<Guid> ProductIds { get; set; } = [];

    /// <summary>
    /// Added to the product's base price. May be negative — "small" is often a discount, and a
    /// negative one is what <c>GlobalVariationAttach.Fits</c> exists to bound.
    /// </summary>
    /// <remarks>
    /// <b><c>required</c>, not defaulted to 0</b> (Sonar S6964): 0 is a legitimate value, so an
    /// omitted field and a deliberate "no surcharge" would be the same payload, and the admin who
    /// forgot the price of a Large on forty products would be told nothing.
    /// </remarks>
    public required decimal PriceModifier { get; set; }
}
