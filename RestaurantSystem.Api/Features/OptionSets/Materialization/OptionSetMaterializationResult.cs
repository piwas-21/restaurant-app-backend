namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationResult
{
    public Guid OptionSetId { get; set; }
    public int SetVersion { get; set; }
    public List<OptionSetMaterializationTargetResultDto> Targets { get; set; } = [];
}
