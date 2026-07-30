using RestaurantSystem.Api.Common.Modules;

namespace RestaurantSystem.Api.Features.Setup;

/// <summary>
/// One step of the first-run setup checklist.
/// </summary>
/// <param name="Key">
/// Stable identifier. Also the i18n key stem on the frontend, and — for acknowledgeable
/// steps — what is stored in <c>SetupChecklistState.AcknowledgedSteps</c>. Never rename
/// one; retire it and add a new key, or every tenant's stored acknowledgement silently
/// stops matching.
/// </param>
/// <param name="ModuleId">
/// The module that owns this step, or null when every tenant needs it. A step for a
/// module the tenant did not buy is never returned — the surface it points at is 404
/// under <c>RequireModule</c>, and telling an owner to go somewhere that does not exist
/// is worse than saying nothing.
/// </param>
/// <param name="IsDerived">
/// True when completion is OBSERVED from real data rather than claimed. Derived steps
/// cannot be acknowledged (see <c>SetupSteps.Acknowledgeable</c>).
/// </param>
public sealed record SetupStep(string Key, string? ModuleId, bool IsDerived);

/// <summary>
/// The first-run checklist a new owner is walked through (SOFRA-ONBOARDING-PLAN O4).
/// Definition of done for O4: a new owner takes their first real order without the
/// founder on a call.
/// </summary>
/// <remarks>
/// The split between derived and acknowledged steps is the load-bearing decision here,
/// and it is forced by what provisioning already does. <c>MigrationExtensions</c> seeds
/// working hours, restaurant info, a floor plan and 18 tables into every fresh tenant
/// database. So a "do they have opening hours?" query answers YES one second after the
/// tenant boots, and a checklist built on it would congratulate an owner for work
/// nobody did. Those steps are acknowledged instead.
/// <para>
/// What a fresh tenant genuinely has none of is menu content and staff:
/// <c>E2EMenuFixtureSeeder</c> is opt-in and off, and <c>UserSeeder</c> creates exactly
/// one admin. Those two steps are therefore derived, and cannot be faked.
/// </para>
/// <para>
/// There is deliberately no "upload your logo" step: the tenant app has no logo
/// surface at all — no field on <c>RestaurantInfo</c>, no upload anywhere in
/// <c>/admin</c>. The branding control that does exist is the theme palette, which is
/// what <c>appearance</c> points at.
/// </para>
/// </remarks>
public static class SetupSteps
{
    public const string RestaurantInfo = "restaurant-info";
    public const string OpeningHours = "opening-hours";
    public const string Appearance = "appearance";
    public const string Menu = "menu";
    public const string TablesQr = "tables-qr";
    public const string Staff = "staff";
    public const string KitchenBoard = "kitchen-board";
    public const string Cashier = "cashier";
    public const string Server = "server";
    public const string Reservations = "reservations";
    public const string Loyalty = "loyalty";
    public const string Printing = "printing";

    /// <summary>
    /// Every step, in the order an owner should work through them: confirm who you are,
    /// when you are open and what you look like; then the menu, which is the long one;
    /// then the things that carry an order — tables, staff, and the modules they bought.
    /// </summary>
    public static readonly IReadOnlyList<SetupStep> All =
    [
        new(RestaurantInfo, null, IsDerived: false),
        new(OpeningHours, null, IsDerived: false),
        new(Appearance, null, IsDerived: false),
        // Derived: a fresh tenant has no categories and no products.
        new(Menu, null, IsDerived: true),
        new(TablesQr, null, IsDerived: false),
        // Derived: UserSeeder creates exactly one admin and nothing else.
        new(Staff, null, IsDerived: true),
        // One step per bought module, so no module a tenant paid for goes unmentioned.
        new(KitchenBoard, ModuleIds.KitchenBoard, IsDerived: false),
        new(Cashier, ModuleIds.Cashier, IsDerived: false),
        new(Server, ModuleIds.Server, IsDerived: false),
        new(Reservations, ModuleIds.Reservations, IsDerived: false),
        new(Loyalty, ModuleIds.Loyalty, IsDerived: false),
        new(Printing, ModuleIds.Printing, IsDerived: false),
    ];

    private static readonly HashSet<string> AcknowledgeableKeys =
        new(All.Where(s => !s.IsDerived).Select(s => s.Key), StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="key"/> is a step an admin may mark done by hand.
    /// </summary>
    /// <remarks>
    /// False for a derived step, and the caller must reject rather than ignore that.
    /// Accepting an acknowledgement on <c>menu</c> would let an owner tick off a menu
    /// they never built, which is the one thing this checklist exists to prevent —
    /// "nothing left to do" has to be earned. An owner who wants the checklist gone
    /// without finishing it dismisses the whole thing, which is honest about what
    /// happened.
    /// </remarks>
    public static bool IsAcknowledgeable(string key) =>
        !string.IsNullOrWhiteSpace(key) && AcknowledgeableKeys.Contains(key);

    /// <summary>The steps this instance's modules entitle the tenant to see.</summary>
    public static IEnumerable<SetupStep> For(ITenantModules modules) =>
        All.Where(s => IsEntitled(s, modules));

    /// <summary>
    /// Whether this instance's modules entitle the tenant to <paramref name="key"/>.
    /// Unknown keys are not entitled.
    /// </summary>
    /// <remarks>
    /// Checked on WRITE as well as read, which is not redundant. Filtering only on read
    /// would let an acknowledgement for an unbought module be stored happily and sit
    /// there invisibly — and then, the day the tenant upgrades and the step finally
    /// appears, it would appear already ticked. The owner would never be walked through
    /// setting up the module they just paid for, and nothing anywhere would look wrong.
    /// </remarks>
    public static bool IsEntitledTo(string key, ITenantModules modules)
    {
        var step = All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
        return step is not null && IsEntitled(step, modules);
    }

    private static bool IsEntitled(SetupStep step, ITenantModules modules) =>
        step.ModuleId is null || modules.IsEnabled(step.ModuleId);
}
