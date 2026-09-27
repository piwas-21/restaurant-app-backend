using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class CreateOrReuseImportedSetRequest
{
    public string SourceTemplateId { get; set; } = string.Empty;
    public int SourceRevision { get; set; }
    public string SourceOptionSetId { get; set; } = string.Empty;
    public OptionSetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = "en";
    public IReadOnlyDictionary<string, string> Translations { get; set; } = new Dictionary<string, string>();
    public TranslationOwnerMetadataDto? TranslationMetadata { get; set; }
    public IReadOnlyList<ImportedOptionSetEntryRequest> Entries { get; set; } = [];
    public IReadOnlySet<Guid> StagedProductIds { get; set; } = new HashSet<Guid>();
}
