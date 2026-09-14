using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public class PrinterFeedSettings
{
    public const string SectionName = "PrinterFeed";

    [Range(1, int.MaxValue - 1)]
    public int UpdatePageSize { get; set; } = 50;
}
