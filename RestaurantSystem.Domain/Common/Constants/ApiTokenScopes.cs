namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>
/// The permission vocabulary a machine API token can be granted
/// (workspace docs/plans/API-TOKENS-PLAN.md §2).
/// </summary>
/// <remarks>
/// Strings rather than an enum because they are PERSISTED (an <c>ApiToken.Scopes</c> text[])
/// and handed to clients verbatim; renaming an enum member would silently re-interpret every
/// token already issued.
/// <para>
/// Every value here is backed by endpoints that exist. A scope with no endpoint behind it is
/// worse than no scope: it reads as a granted permission and enforces nothing.
/// </para>
/// </remarks>
public static class ApiTokenScopes
{
    /// <summary>Read categories, products, menus and global ingredients.</summary>
    public const string MenuRead = "menu:read";

    /// <summary>Create / update / delete categories, products, menus and global ingredients.</summary>
    public const string MenuWrite = "menu:write";

    /// <summary>Read orders.</summary>
    public const string OrdersRead = "orders:read";

    /// <summary>Advance or cancel an order. Never refunds — those move money.</summary>
    public const string OrdersWrite = "orders:write";

    /// <summary>Read reservations.</summary>
    public const string ReservationsRead = "reservations:read";

    /// <summary>Update, confirm or cancel a reservation.</summary>
    public const string ReservationsWrite = "reservations:write";

    /// <summary>Read restaurant info, working hours, order types and enabled modules.</summary>
    public const string TenantRead = "tenant:read";

    /// <summary>
    /// Edit the restaurant's own profile: address, phone numbers, logo and opening hours.
    /// Deliberately NOT tax configuration, order-type configuration or form fields — those
    /// change how the restaurant charges and serves people, and no machine client has asked
    /// for them (workspace docs/plans/API-TOKENS-PLAN.md §2).
    /// </summary>
    public const string TenantWrite = "tenant:write";

    /// <summary>
    /// Every scope that may be granted. The create-token validator rejects anything outside
    /// this set, so a typo is a 400 rather than a token that silently grants nothing.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        MenuRead, MenuWrite,
        OrdersRead, OrdersWrite,
        ReservationsRead, ReservationsWrite,
        TenantRead, TenantWrite
    };

    /// <summary>Whether <paramref name="scope"/> is part of the vocabulary. Case-sensitive on purpose.</summary>
    public static bool IsKnown(string? scope) => scope is not null && All.Contains(scope);
}
