namespace RestaurantSystem.Api.Settings;

public sealed class MenuAuthoringPaginationSettings
{
    public const string SectionName = "MenuAuthoringPagination";

    public int MinimumPageSize { get; set; } = 1;
    public int DefaultPageSize { get; set; } = 24;
    public int MaximumPageSize { get; set; } = 100;
    public int MaximumSearchQueryLength { get; set; } = 120;
    public int MinimumNormalizedSearchQueryLength { get; set; } = 2;
    public int MaximumNormalizedSearchQueryLength { get; set; } = 160;
    public int MaximumSearchAliasLength { get; set; } = 160;
    public int MaximumSearchCursorLength { get; set; } = 2048;

    public int Normalize(int? requestedPageSize) =>
        Math.Clamp(requestedPageSize ?? DefaultPageSize, MinimumPageSize, MaximumPageSize);
}
