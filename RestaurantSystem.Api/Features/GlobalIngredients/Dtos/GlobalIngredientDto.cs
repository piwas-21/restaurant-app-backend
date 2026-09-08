using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

public record GlobalIngredientDto
{
    public Guid Id { get; set; }
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }

    // Ingredient or sauce (S5). The picker needs it to offer a library row to the right group, so
    // it is on the read AND both write shapes. Omitting it keeps creating ingredients — the seeder
    // stamps only its curated 39-row sauce family as Sauce (ReclassifySeededSauces backfills
    // databases seeded before that stamp existed).
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;

    /// <summary>
    /// Archived (plan D4): off the shelf, still linked. NOT the same as soft-deleted — an archived
    /// row is readable, restorable, and keeps serving the products that already reference it.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>The "no X" answer for its kind (e.g. "Sans Sauces") — exclusive in the guest sheet.</summary>
    public bool IsNoneOption { get; set; }

    /// <summary>Platform seed or this tenant's own — see <see cref="LibraryOrigin"/>.</summary>
    public LibraryOrigin Origin { get; set; }

    /// <summary>
    /// "used on N items": distinct live products whose ingredients link to this row. Counted for
    /// the whole page in one aggregate query, never one query per row.
    /// </summary>
    public int UsedOnProductCount { get; set; }

    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}

public record GlobalIngredientTranslationDto
{
    public string LanguageCode { get; set; } = null!;
    public string Name { get; set; } = null!;
}
