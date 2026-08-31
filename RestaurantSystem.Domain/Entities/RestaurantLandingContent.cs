using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Optional landing-page copy for one configured restaurant language.</summary>
public class RestaurantLandingContent : Entity
{
    public Guid RestaurantInfoId { get; set; }
    public required string LanguageCode { get; set; }
    public string? HeroEyebrow { get; set; }
    public string? WelcomeTitle { get; set; }
    public string? WelcomeBody { get; set; }
    public string? StoryTitle { get; set; }
    public string? StoryBody { get; set; }

    public virtual RestaurantInfo RestaurantInfo { get; set; } = null!;
}

/// <summary>How a landing page obtains its background image.</summary>
public enum LandingBackgroundMode
{
    Default,
    Custom,
    None,
}
