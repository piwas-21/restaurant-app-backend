namespace RestaurantSystem.Api.Features.GlobalVariations.Dtos;

public record GlobalVariationDto
{
    public Guid Id { get; set; }
    public string DefaultName { get; set; } = null!;
    public bool IsActive { get; set; }

    /// <summary>Archived (plan D4): off the shelf, still linked, restorable. Not soft-deleted.</summary>
    public bool IsArchived { get; set; }

    /// <summary>"used on N items" — distinct live products whose variations link to this row.</summary>
    public int UsedOnProductCount { get; set; }

    public List<GlobalVariationTranslationDto> Translations { get; set; } = [];
}

public record GlobalVariationTranslationDto
{
    public string LanguageCode { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public record CreateGlobalVariationDto
{
    public string DefaultName { get; set; } = null!;
    public List<GlobalVariationTranslationDto> Translations { get; set; } = [];
}

public record UpdateGlobalVariationDto
{
    public string DefaultName { get; set; } = null!;

    /// <summary>
    /// Nullable on purpose, unlike the ingredient library's (backend #428): a PUT that simply omits
    /// the field must leave availability alone rather than binding <c>false</c> and hiding the row
    /// from every screen with no way to find it again.
    /// </summary>
    public bool? IsActive { get; set; }

    public List<GlobalVariationTranslationDto> Translations { get; set; } = [];
}
