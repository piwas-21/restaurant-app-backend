namespace RestaurantSystem.Api.Common.Models;

/// <summary>
/// Stable machine-readable error codes carried on <see cref="ApiResponse{T}.ErrorCode"/>.
///
/// Codes are intentionally English PascalCase strings (not enums) so they remain
/// constant across backend localisation changes and survive JSON round-trips
/// without depending on enum serialization config.
///
/// Adding a code is a public API contract change — coordinate with the
/// frontend (and any other API consumer) before introducing one.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Returned by the customer registration endpoint when an account with the
    /// submitted email already exists. Frontend uses this to surface an inline
    /// "Email already registered" hint without substring-matching the message.
    /// </summary>
    public const string EmailAlreadyExists = "EmailAlreadyExists";

    /// <summary>
    /// Returned when an item cannot be ordered through the basket's current order type. The
    /// message names the channels the item IS available on, and the frontend re-displays it
    /// verbatim — this code is what tells it that the message is safe to show a guest, rather
    /// than it having to trust every 400 on the endpoint.
    /// </summary>
    public const string OrderTypeNotAvailable = "OrderTypeNotAvailable";

    /// <summary>
    /// Returned with a 404 when the endpoint belongs to a product module this tenant
    /// did not buy (sofra ADR-010 / S11, <see cref="Modules.RequireModuleAttribute"/>).
    /// It is what lets the frontend tell "this restaurant has no reservations module"
    /// apart from "that reservation id does not exist" — both are 404 on the wire.
    /// </summary>
    public const string ModuleNotEnabled = "ModuleNotEnabled";

    /// <summary>
    /// Returned by <c>PUT|DELETE /api/Basket/items/{id}</c> with a 404 when the BASKET ROW itself is
    /// gone — the cleanup service reaped it, or the session id expired — so the request never got as
    /// far as looking at an item. A real failure the guest must be told about: the client cannot
    /// silently resync, because <c>GetBasketQuery</c> answers a missing basket with an empty basket
    /// and a SUCCESS, so resyncing here replaces the whole cart with "Your cart is empty".
    /// </summary>
    /// <remarks>
    /// Scoped to those two endpoints on purpose. <c>BasketService</c> raises the same
    /// "Basket not found" from <c>ClearBasketAsync</c> and <c>RemovePromoCodeAsync</c>, and both are
    /// left UNCODED: the first is still wrapped by a catch-all handler that answers 200, and the
    /// second sits behind a stub route, so a code on either could never reach a client. Each throw
    /// site carries a comment saying so. Tag them when that stops being true, not before.
    /// </remarks>
    public const string BasketNotFound = "BasketNotFound";

    /// <summary>
    /// Returned by <c>PUT|DELETE /api/Basket/items/{id}</c> with a 404 when the basket exists but
    /// the addressed ITEM does not — normally because the guest already removed it in another tab.
    /// This is the one basket 404 a client may treat as benign and recover from by resyncing.
    /// Paired with <see cref="BasketNotFound"/>: both are a 404 on the same endpoint, and telling
    /// them apart is the entire reason these two codes exist (frontend issue #415).
    /// </summary>
    public const string BasketItemNotFound = "BasketItemNotFound";

    /// <summary>
    /// Returned with a 400 when an add-to-basket names a product that hides its base row
    /// (<c>Product.HideBaseProduct</c>) but chooses no variation. The client hides that option, so
    /// reaching this means a stale tab, the waiter/POS de-select, or a crafted payload; the code is
    /// what lets the client re-open the picker instead of showing a generic failure.
    /// </summary>
    public const string VariationRequired = "VariationRequired";
}
