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
    public bool IsActive { get; set; }
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;
    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}
