using System.Runtime.Serialization;

namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// Who put a library row on the shelf — the platform's own seed, or this tenant.
///
/// <para>
/// It applies to both reusable catalogs (<see cref="Entities.GlobalIngredient"/> and
/// <see cref="Entities.GlobalVariation"/>), which are per-tenant TABLES seeded with platform rows.
/// That is exactly why the distinction could not be read off the data: a name an admin typed and a
/// name we shipped are the same shape in the same table, so the picker offered "Delete" on the 654
/// seeded ingredients and the 50 seeded variations as readily as on the tenant's own three.
/// </para>
///
/// <para>
/// <b><see cref="System"/> is 0 on purpose</b>, and the migration adds the column with that default:
/// every row that exists when it lands was seeded, so no backfill is needed and no query has to
/// know when the column arrived. It also fails safe — a writer that forgets to stamp
/// <see cref="Custom"/> produces a row that can be archived but not deleted, which is recoverable,
/// where the opposite would let a built-in be removed.
/// </para>
///
/// <para>
/// What it changes is one rule, stated once in each delete handler: a <see cref="System"/> row is
/// ARCHIVED whatever its usage count, never removed. Archiving stays available for both, because
/// "we do not sell that" is a thing a tenant must be able to say about a shipped row.
/// </para>
/// </summary>
public enum LibraryOrigin
{
    /// <summary>Seeded by the platform. Archivable, never deletable.</summary>
    [EnumMember(Value = "system")]
    System = 0,

    /// <summary>Created in this tenant — by the library picker, or promoted from a product's own row.</summary>
    [EnumMember(Value = "custom")]
    Custom = 1
}
