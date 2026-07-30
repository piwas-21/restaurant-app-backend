using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// The tenant's first-run setup checklist state (sofra SOFRA-ONBOARDING-PLAN O4).
/// Singleton table — exactly one row, created lazily on the first write rather than
/// seeded, so "no row" is the honest default for a brand-new tenant.
/// </summary>
/// <remarks>
/// This stores only what CANNOT be observed. Most of the checklist is derived from
/// real data — a tenant with menu categories and products has plainly done the menu
/// step — and derived steps are deliberately not storable here, so nobody can mark
/// one done without doing it.
/// <para>
/// The steps that DO live here are the ones a fresh database already satisfies:
/// <c>MigrationExtensions</c> seeds working hours, restaurant info, a floor plan and
/// tables on every new tenant, so "hours exist" is true one second after provisioning
/// and proves nothing. For those, the owner's acknowledgement is the only real signal.
/// </para>
/// <para>
/// State is per-INSTANCE, not per-user: whether a restaurant is set up is a fact about
/// the restaurant, and a second admin should not be shown a checklist the owner has
/// already worked through.
/// </para>
/// </remarks>
public class SetupChecklistState : Entity
{
    /// <summary>
    /// The one and only id this table ever holds, so the primary key is what enforces
    /// "singleton" instead of a convention nothing checks.
    /// </summary>
    /// <remarks>
    /// Both writers read-then-insert on their own scoped <c>DbContext</c>. Without a
    /// fixed id, two concurrent requests — two admins, or one double-clicked checkbox —
    /// both find no row and both insert, and the table is permanently split. From then
    /// on `FirstOrDefaultAsync` picks whichever row Postgres hands back first, which an
    /// UPDATE can change under MVCC, so acknowledgements and the dismissal flip between
    /// two rows nondeterministically and nothing in the data looks wrong.
    /// <para>
    /// With a fixed id the loser of that race gets a duplicate-key violation instead,
    /// which is a condition the writer can see and recover from.
    /// </para>
    /// </remarks>
    public static readonly Guid SingletonId = new("5e7c9f10-0000-4000-8000-000000000001");

    /// <summary>
    /// When an admin hid the checklist. Null = visible. Reversible on purpose — the
    /// checklist is resumable, so dismissing it is a preference and not a one-way door.
    /// </summary>
    public DateTime? DismissedAt { get; set; }

    /// <summary>
    /// Keys of the steps an admin has marked done (<c>SetupSteps</c> vocabulary).
    /// Only acknowledgeable steps ever appear here; an unrecognised key is ignored on
    /// read, so retiring a step from the catalog cannot break a live tenant.
    /// </summary>
    public List<string> AcknowledgedSteps { get; set; } = [];
}
