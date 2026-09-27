namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class CreateOrReuseImportedSetResult
{
    public Guid OptionSetId { get; set; }
    public int Version { get; set; }
    public bool Created { get; set; }
    public string SourceLocale { get; set; } = "en";
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ImportedOptionSetEntryResult> Entries { get; set; } = [];
}
