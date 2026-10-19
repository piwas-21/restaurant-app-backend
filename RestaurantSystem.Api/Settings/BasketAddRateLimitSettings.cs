using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class BasketAddRateLimitSettings
{
    public const string SectionName = "BasketAdds";
    // Shared by both add routes and all sessions behind one IP, including restaurant Wi-Fi.
    [Range(1, 6000)]
    public int AttemptsPerIpPerMinute { get; set; } = 600;
}
