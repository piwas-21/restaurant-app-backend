namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationJobTargetDto
{
    public int Sequence { get; set; }
    public string TargetKey { get; set; } = string.Empty;
    public Guid TargetProductId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public OptionSetMaterializationTargetResultDto? Result { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
