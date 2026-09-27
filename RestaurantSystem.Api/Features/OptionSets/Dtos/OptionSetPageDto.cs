namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetPageDto
{
    public List<OptionSetSummaryDto> Items { get; set; } = [];
    public string? NextCursor { get; set; }
}
