using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetWriteRequestDto
{
    public required OptionSetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = "en";
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public OptionSetStatus Status { get; set; } = OptionSetStatus.Active;
    public List<OptionSetEntryDto> Entries { get; set; } = [];
    public TranslationOwnerMetadataDto? TranslationMetadata { get; set; }
}
