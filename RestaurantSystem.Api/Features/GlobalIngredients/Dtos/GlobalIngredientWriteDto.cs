using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

// The two WRITE shapes, split out of GlobalIngredientDto.cs when the read shape grew `Origin` and
// the file passed the 60-line DTO limit. Same namespace, so nothing importing them changes; the
// read/write seam is the natural cut, and it is the one the variation library already implies —
// what a client SENDS has never been what it reads back (no id, no counts, no archive state).
//
// `DefaultName` loses its `= null!` in the move (§5.12). `= string.Empty` rather than `required`:
// `required` makes System.Text.Json THROW on a payload that omits the field, turning a 400 the
// validator already produces (`NotEmpty`) into an unhandled binding failure with a different shape.
// Empty binds, the validator refuses it, and the answer the client gets is the one it got before.

public record CreateGlobalIngredientDto
{
    public string DefaultName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;
    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}

public record UpdateGlobalIngredientDto
{
    public string DefaultName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    // Both nullable on purpose (#428): on an UPDATE, absent must mean "unchanged". A non-nullable
    // bool bound `false` and hid the row from every screen; a defaulted `Kind` demoted a sauce.
    // The create DTO above keeps its default, because for a NEW row "ingredient" is the right
    // answer. Reasoning in UpdateGlobalIngredientCommand's param docs.
    public bool? IsActive { get; set; }
    public IngredientKind? Kind { get; set; }

    public List<GlobalIngredientTranslationDto> Translations { get; set; } = [];
}
