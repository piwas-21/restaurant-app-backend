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
    /// Returned with a 404 by <c>PUT /api/Reservations/{id}/mine</c> when the reservation does not
    /// exist, is not owned by the caller, or is a guest booking with no owner at all. One code for
    /// all three on purpose: a distinct 403 would confirm the id exists and turn the route into an
    /// oracle for enumerating real reservations (same argument as <c>GetOrderByIdQuery</c>). It is
    /// also what tells that 404 apart from <see cref="ModuleNotEnabled"/> on the same path.
    /// </summary>
    public const string ReservationNotFound = "ReservationNotFound";

    /// <summary>
    /// Returned with a 400 by <c>PUT /api/Reservations/{id}/mine</c> when the reservation exists and
    /// is the caller's, but its current state forbids a self-service edit: it is Cancelled,
    /// Completed or NoShow, or the booked day is already behind the restaurant's own "today".
    /// The client should stop offering the edit form and offer a new booking instead.
    /// </summary>
    public const string ReservationNotEditable = "ReservationNotEditable";

    /// <summary>
    /// Returned with a 400 by <c>PUT /api/Reservations/{id}/mine</c> when the requested new day is
    /// before the restaurant's own "today". Distinct from <see cref="ReservationNotEditable"/>: the
    /// reservation is editable, the submitted date is not — the client re-opens the date picker.
    /// </summary>
    public const string ReservationDateInPast = "ReservationDateInPast";

    /// <summary>
    /// Returned with a 400 by <c>PUT /api/Reservations/{id}/mine</c> when the new party size exceeds
    /// the capacity of the table the reservation already sits on. Self-service never re-assigns a
    /// table, so the client's recovery is a smaller party or a phone call — not a retry.
    /// </summary>
    public const string ReservationTableCapacityExceeded = "ReservationTableCapacityExceeded";

    /// <summary>
    /// Returned with a 400 by <c>PUT /api/Reservations/{id}/mine</c> when the new day/time overlaps
    /// another live booking on the same table. The client's recovery is a different time, so it is
    /// deliberately NOT the same code as <see cref="ReservationTableCapacityExceeded"/>.
    /// </summary>
    public const string ReservationSlotUnavailable = "ReservationSlotUnavailable";

    /// <summary>
    /// Returned with a 400 when an add-to-basket names a product that hides its base row
    /// (<c>Product.HideBaseProduct</c>) but chooses no variation. The client hides that option, so
    /// reaching this means a stale tab, the waiter/POS de-select, or a crafted payload; the code is
    /// what lets the client re-open the picker instead of showing a generic failure.
    /// </summary>
    public const string VariationRequired = "VariationRequired";

    /// <summary>
    /// Returned with a 400 when an add-to-basket names a COMPONENT product
    /// (<c>Product.IsComponent</c>) as a top-level line. A component exists only to be chosen
    /// inside a bundle section, so the client's recovery is to order the bundle — not to retry.
    /// Distinct from <see cref="VariationRequired"/>: that one says "choose an option on this
    /// item", this one says "this item is itself an option".
    /// </summary>
    public const string ComponentNotOrderable = "ComponentNotOrderable";

    /// <summary>
    /// Returned with a 400 by <c>POST /api/Auth/apple-login</c> when the Apple identity token
    /// fails verification — bad signature, wrong issuer or audience, expired, unsigned. One code
    /// for every cause on purpose: which check failed is a server-log detail, not something to
    /// tell an unauthenticated caller.
    /// </summary>
    public const string InvalidAppleToken = "InvalidAppleToken";

    /// <summary>
    /// Returned with a 503 by <c>POST /api/Auth/apple-login</c> when the refusal is OURS, not the
    /// token's: Apple sign-in is unconfigured on this deployment, or Apple's key endpoint could
    /// not be reached. It is what lets a client say "try again later" instead of "your Apple
    /// account was rejected", and it is deliberately distinct from a rejected token so a
    /// misconfigured box is visible rather than silently blaming every user.
    /// </summary>
    public const string AppleLoginUnavailable = "AppleLoginUnavailable";

    /// <summary>
    /// Returned with a 400 by <c>POST /api/Auth/set-password</c> when the signed-in account
    /// ALREADY has a password. That endpoint exists only for social-login accounts, which have no
    /// password hash to verify; letting it overwrite an existing password would hand anyone with a
    /// stolen access token a silent takeover, since <c>change-password</c> deliberately demands the
    /// current password. The client uses this code to switch the screen to the change-password
    /// flow instead of showing a generic failure — and it must, because the state it encodes
    /// ("your account already has one") is not something the user did wrong on the form.
    /// </summary>
    public const string PasswordAlreadySet = "PasswordAlreadySet"; // pragma: allowlist secret (an error code, not a credential)

    /// <summary>
    /// Returned with a 403 when a caller authenticated by an API TOKEN reaches an endpoint the
    /// token's scope set does not cover — including every endpoint that carries no
    /// <c>[ApiScope]</c> at all, which a token may never reach (API-TOKENS-PLAN §5). It is what
    /// lets a machine client tell "my credential is wrong" (401) from "my credential is fine but
    /// too narrow" (this) without parsing prose.
    /// </summary>
    public const string MissingScope = "MissingScope";
}
