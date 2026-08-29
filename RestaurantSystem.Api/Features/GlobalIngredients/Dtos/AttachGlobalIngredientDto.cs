using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

/// <summary>
/// The body of <c>POST /api/global-ingredients/{id}/attach</c> — plan S8, "reuse at scale".
/// </summary>
/// <remarks>
/// <para>
/// <b>Explicit product ids, never a category.</b> "Apply to every pizza" is resolved by the CLIENT
/// into the ids it is about to send, so the blast-radius confirm (plan D6) and the payload are the
/// same list by construction. A <c>categoryId</c> target would be re-resolved at save time, and a
/// product added to that category between the confirm and the save would be changed by an action
/// the admin never saw.
/// </para>
/// <para>
/// The fields below are what plan D1 says the PRODUCT row owns — price, optionality, max quantity,
/// and now the group. The name and the nine translations are COPIED from the library row instead
/// (D3: provenance, not propagation).
/// </para>
/// </remarks>
public class AttachGlobalIngredientDto
{
    public List<Guid> ProductIds { get; set; } = [];

    /// <summary>WHICH GROUP the copied rows land in — Ingredients or Sauces — stated by the caller.</summary>
    /// <remarks>
    /// One rule, shared with the picker: <b>the ACTION states the kind, and the library row's kind is
    /// only the DEFAULT when it does not</b> — <see cref="Services.GlobalIngredientAttach.CopyOnto"/>
    /// holds the argument. <b>Nullable, and <c>null</c> is not <c>ingredient</c>:</b> "omitted" and
    /// "put these in the Ingredients group" must not be the same payload, the rule
    /// <c>UpdateGlobalIngredientDto.Kind</c> keeps for the same reason (backend #428). Omitted falls
    /// back to the library row's own kind, so a client written before this field keeps the behaviour
    /// it was written against.
    /// </remarks>
    public IngredientKind? Kind { get; set; }

    /// <summary>
    /// Must be <c>true</c>. A REQUIRED ingredient added to a product is retroactively rendered as a
    /// REMOVAL on every order placed before the S1 snapshot — see
    /// <c>AttachGlobalIngredientCommandValidator</c>, which owns the reasoning and the refusal.
    /// </summary>
    public bool IsOptional { get; set; } = true;

    /// <summary>
    /// <b><c>required</c>, and so is <see cref="IsIncludedInBasePrice"/> — every field that moves
    /// money must be STATED.</b> Sonar S6964 flagged exactly the fields with no initializer, and
    /// they were exactly the money ones: an omitted <c>price</c> binds to 0 and quietly makes an
    /// extra free on forty products, while an omitted <c>isIncludedInBasePrice</c> decides whether
    /// <c>BasketPricingService</c> DEDUCTS that price when the guest deselects it. The two fields
    /// that keep a default keep it because their default is safe and meaningful: the attach may only
    /// create OPTIONAL rows at all, and 1 is the minimum quantity.
    /// </summary>
    public required decimal Price { get; set; }

    public int MaxQuantity { get; set; } = 1;

    /// <summary>See <see cref="Price"/> for why this is required rather than defaulted to false.</summary>
    public required bool IsIncludedInBasePrice { get; set; }
}
