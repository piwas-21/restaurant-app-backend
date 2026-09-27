namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed class MenuAuthoringMatchDecisionRequestDto
{
    public string Query { get; set; } = string.Empty;
    public string CandidateType { get; set; } = string.Empty;
    public Guid CandidateId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Alias { get; set; }
}
