namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationJobDto
{
    public Guid JobId { get; set; }
    public Guid OptionSetId { get; set; }
    public int SetVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public List<OptionSetMaterializationJobTargetDto> Targets { get; set; } = [];
}
