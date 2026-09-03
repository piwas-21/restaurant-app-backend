using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

/// <summary>
/// Gives a hand-typed product variation a home in the tenant's own library (plan D14).
///
/// <para>
/// The complaint this answers: a variation typed straight into "Pricing &amp; variations" was saved
/// on the product and nowhere else, so the same three sizes were retyped — translations and all —
/// on every dish that offered them, while the library screen sat one click away and stayed empty of
/// them. The library picker's own "+ Create new" was the only path that ever wrote a row.
/// </para>
///
/// <para>
/// <b>Match first, create second.</b> A name already on the shelf is LINKED to, so re-saving the
/// same product converges instead of piling up duplicates, and two dishes that both offer "Large"
/// end up pointing at one row. Matching is case-insensitive on the trimmed name, which is the only
/// key either side has — the catalog carries no code, and a variation's price is per product.
/// </para>
///
/// <para>
/// <b>An ARCHIVED row is neither linked nor duplicated.</b> Archiving is how an admin takes a name
/// off the shelf, and <see cref="GlobalVariationProvenance"/> already refuses new links to one; the
/// alternatives here are to re-link it (undoing the archive by a side door) or to create a second
/// row with the same name (two shelves, one word). So the variation simply saves unpromoted, which
/// is exactly what it did before this existed.
/// </para>
///
/// <para>
/// <b>The translations travel with the name.</b> They are the whole value of a library row — a pick
/// copies nine names the admin would otherwise retype — so promoting a bare <c>DefaultName</c> would
/// have filled the shelf with untranslated words and left the retyping exactly where it was. The
/// per-language names the admin has just typed on the product ARE the row's translations.
/// </para>
///
/// <para>
/// Everything is decided in ONE query and at most one insert batch per save, never per variation.
/// </para>
///
/// <para>
/// <b>Match-first is not a lock, and there is deliberately no unique index behind it.</b> Two
/// product saves racing on the same new name both find nothing and both insert, leaving two rows
/// with the same word — which the NEXT save then collapses to whichever the database returns first.
/// A partial unique index on <c>lower(default_name)</c> would make that deterministic (measured: the
/// seeded 50 + 654 rows hold no case-insensitive duplicate, so it would build), and it is not here
/// because it converts a benign duplicate on a shelf the admin can tidy into a FAILED PRODUCT SAVE
/// for the same race, which would need a catch-and-re-look-up in the write path. The duplicate is
/// the cheaper failure; the index is the follow-up if the shelf is ever seen to fill.
/// </para>
/// </summary>
internal sealed class CustomVariationPromotion
{
    private readonly IReadOnlyDictionary<string, Guid> _byName;

    private CustomVariationPromotion(IReadOnlyDictionary<string, Guid> byName) => _byName = byName;

    /// <summary>An instance that promotes nothing — for a payload that carries no variations.</summary>
    public static CustomVariationPromotion None { get; } = new(new Dictionary<string, Guid>());

    /// <summary>
    /// One variation the payload does not link to a library row: its default name, and the
    /// per-language names typed alongside it, which become the promoted row's translations.
    /// </summary>
    private readonly record struct Unlinked(string Name, IReadOnlyDictionary<string, string> Translations)
    {
        /// <summary>Adapts the write DTO's `Content` map, which is nullable on both write shapes.</summary>
        public static Unlinked From(string name, Dictionary<string, ProductVariationContentDto>? content) =>
            new(name, content is null
                ? EmptyTranslations
                : content
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Name))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Name));
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyTranslations =
        new Dictionary<string, string>();

    /// <summary>
    /// Resolve (and where needed create) a library row for every unlinked name in the payload.
    /// </summary>
    /// <param name="payload">
    /// Every variation in the payload, linked or not. The UNLINKED ones are what this promotes, and
    /// the filter lives here rather than at each of the three call sites — those sit in two files
    /// the file-length baseline already carries, and the rule is the service's to state anyway. A
    /// blank name is skipped too: the validator refuses it, and a library row called "" helps nobody.
    /// </param>
    /// <remarks>
    /// The rows are ADDED to the change tracker, not saved — the caller's own
    /// <c>SaveChangesAsync</c> commits them inside the same transaction as the product, so a product
    /// write that fails leaves no orphan library rows behind.
    /// </remarks>
    public static async Task<CustomVariationPromotion> PrepareAsync(
        ApplicationDbContext context,
        IEnumerable<(Guid? Link, string Name, Dictionary<string, ProductVariationContentDto>? Content)> payload,
        string auditId,
        CancellationToken cancellationToken)
    {
        var wanted = payload
            .Where(entry => !entry.Link.HasValue)
            .Select(entry => Unlinked.From(entry.Name, entry.Content))
            .Select(entry => entry with { Name = entry.Name?.Trim() ?? string.Empty })
            .Where(entry => entry.Name.Length > 0)
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (wanted.Count == 0)
        {
            return None;
        }

        var lowered = wanted.Select(entry => entry.Name.ToLowerInvariant()).ToList();

        // Archived rows are read too, so a name that is off the shelf can be recognised as taken
        // rather than duplicated — see the class remarks.
        var existing = await context.GlobalVariations
            .Where(g => lowered.Contains(g.DefaultName.ToLower()))
            .Select(g => new { g.Id, g.DefaultName, IsArchived = g.ArchivedAt != null })
            .ToListAsync(cancellationToken);

        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in existing)
        {
            taken.Add(row.DefaultName);
            if (!row.IsArchived)
            {
                byName.TryAdd(row.DefaultName, row.Id);
            }
        }

        foreach (var entry in wanted.Where(entry => !taken.Contains(entry.Name)))
        {
            var created = new GlobalVariation
            {
                // Client-generated, as `CreateProductCommand` and every sibling create handler do.
                // The column carries `gen_random_uuid()` as its default, so leaving `Id` at
                // `Guid.Empty` would have the DATABASE choose it — and this map, built before
                // `SaveChangesAsync`, would hand every promoted variation an empty foreign key.
                Id = Guid.NewGuid(),
                DefaultName = entry.Name,
                IsActive = true,
                Origin = LibraryOrigin.Custom,
                CreatedBy = auditId,
                Translations = entry.Translations
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                    .Select(pair => new GlobalVariationTranslation
                    {
                        LanguageCode = pair.Key,
                        Name = pair.Value.Trim(),
                        CreatedBy = auditId,
                    })
                    .ToList(),
            };
            context.GlobalVariations.Add(created);
            byName[entry.Name] = created.Id;
        }

        return new CustomVariationPromotion(byName);
    }

    /// <summary>
    /// The library row for a hand-typed variation, or <c>null</c> when there is none to give — a
    /// blank name, or a name whose only match is archived.
    /// </summary>
    public Guid? IdFor(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return _byName.TryGetValue(trimmed, out var id) ? id : null;
    }
}
