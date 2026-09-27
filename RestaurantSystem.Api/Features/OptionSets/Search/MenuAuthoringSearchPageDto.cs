namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed class MenuAuthoringSearchPageDto
{
    public List<MenuAuthoringSearchCandidateDto> Items { get; set; } = [];
    public string? NextCursor { get; set; }
}
