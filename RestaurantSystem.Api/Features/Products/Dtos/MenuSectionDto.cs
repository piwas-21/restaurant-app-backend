using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record MenuSectionDto
{
    public Guid? Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }

    public bool IsRequired { get; init; }
    public int MinSelection { get; init; }
    public int MaxSelection { get; init; }
    public TranslationOwnerMetadataDto? TranslationMetadata { get; init; }

    private Dictionary<string, MenuSectionTranslationDto>? _translations = new();

    /// <summary>Whether the localized label map appeared in the request.</summary>
    [JsonIgnore]
    public bool TranslationsSpecified { get; private set; }

    /// <summary>Localized section labels and descriptions, keyed by language tag.</summary>
    public Dictionary<string, MenuSectionTranslationDto>? Translations
    {
        get => _translations;
        init
        {
            _translations = value;
            TranslationsSpecified = true;
        }
    }

    private List<MenuSectionItemDto>? _items = new();

    /// <summary>
    /// Whether the nested option list appeared in the JSON body. The authoring PATCH treats an
    /// omitted list as unchanged and an explicit [] as removal; legacy full-replacement writes
    /// continue to treat a missing list as empty.
    /// </summary>
    [JsonIgnore]
    public bool ItemsSpecified { get; private set; }

    public List<MenuSectionItemDto>? Items
    {
        get => _items;
        init
        {
            _items = value;
            ItemsSpecified = true;
        }
    }
}

public sealed record MenuSectionTranslationDto
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
