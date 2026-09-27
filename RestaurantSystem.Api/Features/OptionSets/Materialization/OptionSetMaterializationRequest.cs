using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationRequest
{
    [JsonIgnore]
    public Guid OptionSetId { get; set; }
    public required int ExpectedSetVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public IReadOnlyList<OptionSetMaterializationTargetRequest> Targets { get; set; } = [];
}
