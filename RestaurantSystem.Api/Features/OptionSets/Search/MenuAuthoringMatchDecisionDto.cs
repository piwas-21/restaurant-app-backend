namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed class MenuAuthoringMatchDecisionDto
{
    public string NormalizedName { get; set; } = string.Empty;
    public string CandidateType { get; set; } = string.Empty;
    public Guid CandidateId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Alias { get; set; }
}
