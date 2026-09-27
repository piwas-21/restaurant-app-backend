namespace RestaurantSystem.Api.Settings;

public sealed class MenuAuthoringPaginationSettings
{
    public const string SectionName = "MenuAuthoringPagination";

    public int MinimumPageSize { get; set; } = 1;
    public int DefaultPageSize { get; set; } = 24;
    public int MaximumPageSize { get; set; } = 100;

    public int Normalize(int? requestedPageSize) =>
        Math.Clamp(requestedPageSize ?? DefaultPageSize, MinimumPageSize, MaximumPageSize);
}
