namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class ImportedOptionSetEntryResult
{
    public string SourceEntryId { get; set; } = string.Empty;
    public Guid OptionSetEntryId { get; set; }
}
