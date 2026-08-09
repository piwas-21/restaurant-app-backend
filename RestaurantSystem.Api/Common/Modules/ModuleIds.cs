namespace RestaurantSystem.Api.Common.Modules;

/// <summary>
/// The product-module vocabulary (sofra ADR-010).
///
/// These exact strings appear in three other places and must stay identical in all
/// four, or a tenant's registry entry stops meaning what it says:
///   - deploy repo  tenants/registry.yml       (the `modules:` list itself)
///   - deploy repo  provision-tenant.sh        (KNOWN_MODULES — fails loudly on a typo)
///   - sofra        lib/module-catalog.ts      (MODULE_IDS + the list price of each)
///
/// <c>extra-languages</c> is a PRICING rule rather than a gated surface — it is part
/// of the vocabulary so an entry carrying it is recognised, but nothing gates on it.
///
/// <c>online-payments</c> gates <c>PaymentsController</c> at the class level (S4), but it
/// still has no <c>ROUTE_MODULE_ENTRIES</c> row on the frontend: it reveals no PAGE, it
/// turns a payment METHOD on inside the existing checkout. The gate is therefore only half
/// the enforcement — the availability endpoint (S8) ANDs this flag with an actually-
/// configured Stripe account, and both halves matter because both module gates fail OPEN.
/// </summary>
public static class ModuleIds
{
    public const string Core = "core";
    public const string KitchenBoard = "kitchen-board";
    public const string Cashier = "cashier";
    public const string Server = "server";
    public const string Reservations = "reservations";
    public const string Loyalty = "loyalty";
    public const string Printing = "printing";
    public const string OnlinePayments = "online-payments";
    public const string ExtraLanguages = "extra-languages";

    /// <summary>Every id above, in catalog order. Case-insensitive membership.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Core, KitchenBoard, Cashier, Server, Reservations, Loyalty, Printing, OnlinePayments,
        ExtraLanguages,
    };

    private static readonly HashSet<string> Lookup =
        new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="moduleId"/> is part of the catalog vocabulary.</summary>
    public static bool IsKnown(string moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId) && Lookup.Contains(moduleId);
}
