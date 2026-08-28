using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

public record GlobalIngredientDto
{
    public Guid Id { get; set; }
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }

    // Ingredient or sauce (S5). The picker needs it to offer a library row to the right group, so
    // it is on the read AND both write shapes. Omitting it keeps creating ingredients, which is
    // what all 654 seeded rows are.
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;

    /// <summary>
    /// Archived (plan D4): off the shelf, still linked. NOT the same as soft-deleted — an archived
    /// row is readable, restorable, and keeps serving the products that already reference it.
    /// </summary>
    public bool IsArchived { get; set; }

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

public record CreateGlobalIngredientDto
{
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;
    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}

public record UpdateGlobalIngredientDto
{
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }

    // Both nullable on purpose (#428): on an UPDATE, absent must mean "unchanged". A non-nullable
    // bool bound `false` and hid the row from every screen; a defaulted `Kind` demoted a sauce.
    // The create DTO above keeps its default, because for a NEW row "ingredient" is the right
    // answer. Reasoning in UpdateGlobalIngredientCommand's param docs.
    public bool? IsActive { get; set; }
    public IngredientKind? Kind { get; set; }

    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}
