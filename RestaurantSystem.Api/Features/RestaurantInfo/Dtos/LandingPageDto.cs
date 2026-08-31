namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

/// <summary>Public, tenant-specific landing-page configuration.</summary>
public record LandingPageDto(
    string BackgroundMode,
    string? BackgroundImageUrl,
    IReadOnlyDictionary<string, LandingPageContentDto> Content);

/// <summary>Optional copy overrides for one locale. Null makes the client use its bundled fallback.</summary>
public record LandingPageContentDto(
    string? HeroEyebrow,
    string? WelcomeTitle,
    string? WelcomeBody,
    string? StoryTitle,
    string? StoryBody);

/// <summary>One locale supplied by the admin on a full landing-page replacement.</summary>
public record UpdateLandingPageContentDto(
    string? LanguageCode,
    string? HeroEyebrow,
    string? WelcomeTitle,
    string? WelcomeBody,
    string? StoryTitle,
    string? StoryBody);
