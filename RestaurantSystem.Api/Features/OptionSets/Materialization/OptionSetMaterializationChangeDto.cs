namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationChangeDto
{
    public Guid EntryId { get; set; }
    public string RowType { get; set; } = string.Empty;
    public Guid? RowId { get; set; }
    public string Action { get; set; } = string.Empty;
    public List<string> ChangedFields { get; set; } = [];
    public List<string> PreservedFields { get; set; } = [];
}
