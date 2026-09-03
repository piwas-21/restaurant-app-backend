using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

/// <summary>
/// The ingredient/sauce twin of <see cref="CustomVariationPromotion"/>: a row typed straight into
/// "Recipe &amp; dietary" gets a home in the tenant's own library instead of living only on the one
/// product (plan D14). Read that class for the match-first rule and for why an archived name is
/// neither linked nor duplicated; both hold here unchanged.
///
/// <para>
/// <b>The KIND is part of the key, and that is the whole difference.</b> "Garlic" the ingredient and
/// "Garlic sauce" the sauce are separate library rows because the picker offers each catalog to its
/// own group (<see cref="IngredientKind"/> / plan D8), so matching on the name alone would file a
/// hand-typed sauce under Ingredients and the library picker would then never offer it where it
/// belongs.
/// </para>
/// </summary>
internal sealed class CustomIngredientPromotion
{
    private readonly IReadOnlyDictionary<string, Guid> _byKey;

    private CustomIngredientPromotion(IReadOnlyDictionary<string, Guid> byKey) => _byKey = byKey;

    /// <summary>An instance that promotes nothing — for a payload that carries no ingredients.</summary>
    public static CustomIngredientPromotion None { get; } = new(new Dictionary<string, Guid>());

    /// <summary>
    /// One ingredient the payload does not link to a library row: its default name, its kind, and
    /// the per-language names typed alongside it, which become the promoted row's translations —
    /// see <see cref="CustomVariationPromotion"/> for why a bare name would have been half a fix.
    /// </summary>
    private readonly record struct Unlinked(
        string Name,
        IngredientKind Kind,
        IReadOnlyDictionary<string, string> Translations)
    {
        public static Unlinked From(ProductIngredientDto dto) =>
            new(dto.Name, dto.Kind, dto.Content is null
                ? EmptyTranslations
                : dto.Content
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Name))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Name));
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyTranslations =
        new Dictionary<string, string>();

    /// <inheritdoc cref="CustomVariationPromotion.PrepareAsync"/>
    public static async Task<CustomIngredientPromotion> PrepareAsync(
        ApplicationDbContext context,
        IEnumerable<ProductIngredientDto> payload,
        string auditId,
        CancellationToken cancellationToken)
    {
        var wanted = payload
            .Where(dto => !dto.GlobalIngredientId.HasValue)
            .Select(Unlinked.From)
            .Select(entry => entry with { Name = entry.Name?.Trim() ?? string.Empty })
            .Where(entry => entry.Name.Length > 0)
            .GroupBy(entry => KeyOf(entry.Name, entry.Kind), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (wanted.Count == 0)
        {
            return None;
        }

        var lowered = wanted.Select(entry => entry.Name.ToLowerInvariant()).ToList();

        var existing = await context.GlobalIngredients
            .Where(g => lowered.Contains(g.DefaultName.ToLower()))
            .Select(g => new { g.Id, g.DefaultName, g.Kind, IsArchived = g.ArchivedAt != null })
            .ToListAsync(cancellationToken);

        var byKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in existing)
        {
            var key = KeyOf(row.DefaultName, row.Kind);
            taken.Add(key);
            if (!row.IsArchived)
            {
                byKey.TryAdd(key, row.Id);
            }
        }

        foreach (var entry in wanted.Where(entry => !taken.Contains(KeyOf(entry.Name, entry.Kind))))
        {
            var created = new GlobalIngredient
            {
                // Client-generated — see CustomVariationPromotion for why this map cannot wait for
                // the database to choose the id.
                Id = Guid.NewGuid(),
                DefaultName = entry.Name,
                IsActive = true,
                Kind = entry.Kind,
                Origin = LibraryOrigin.Custom,
                CreatedBy = auditId,
                Translations = entry.Translations
                    .Select(pair => new GlobalIngredientTranslation
                    {
                        LanguageCode = pair.Key,
                        Name = pair.Value.Trim(),
                        CreatedBy = auditId,
                    })
                    .ToList(),
            };
            context.GlobalIngredients.Add(created);
            byKey[KeyOf(entry.Name, entry.Kind)] = created.Id;
        }

        return new CustomIngredientPromotion(byKey);
    }

    /// <summary>
    /// The library row for a hand-typed ingredient, or <c>null</c> when there is none to give — a
    /// blank name, or a name-and-kind whose only match is archived.
    /// </summary>
    public Guid? IdFor(string? name, IngredientKind kind)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return _byKey.TryGetValue(KeyOf(trimmed, kind), out var id) ? id : null;
    }

    /// <summary>Case-insensitive on the name, exact on the kind — see the class remarks.</summary>
    private static string KeyOf(string name, IngredientKind kind) => $"{(int)kind}|{name.ToLowerInvariant()}";
}
