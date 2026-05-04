namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

public record GlobalIngredientDto
{
    public Guid Id { get; set; }
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
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
    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}

public record UpdateGlobalIngredientDto
{
    public string DefaultName { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}
