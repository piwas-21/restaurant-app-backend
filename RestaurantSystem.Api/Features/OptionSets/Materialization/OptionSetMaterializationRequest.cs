namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationRequest
{
    public Guid OptionSetId { get; set; }
    public int ExpectedSetVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public IReadOnlyList<OptionSetMaterializationTargetRequest> Targets { get; set; } = [];
}
