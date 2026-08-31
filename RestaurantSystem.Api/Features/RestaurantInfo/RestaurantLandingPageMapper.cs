using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

// The entity, not the enclosing `...Features.RestaurantInfo` namespace. The alias must live AFTER
// the file-scoped namespace declaration: at compilation-unit level the namespace member of
// `...Features` would win and the mapper would not compile.
using RestaurantInfo = RestaurantSystem.Domain.Entities.RestaurantInfo;
using LandingBackgroundMode = RestaurantSystem.Domain.Entities.LandingBackgroundMode;

/// <summary>Maps the landing-only aggregate without exposing profile-edit fields.</summary>
public static class RestaurantLandingPageMapper
{
    public static LandingPageDto ToDto(RestaurantInfo info, string? baseUrl)
    {
        var backgroundImageUrl = info.LandingBackgroundMode == LandingBackgroundMode.Custom
            ? ToAbsoluteUrl(baseUrl, info.InteriorImageUrl)
            : null;

        var content = info.LandingContents
            .OrderBy(item => item.LanguageCode)
            .ToDictionary(
                item => item.LanguageCode,
                item => new LandingPageContentDto(
                    item.HeroEyebrow,
                    item.WelcomeTitle,
                    item.WelcomeBody,
                    item.StoryTitle,
                    item.StoryBody),
                StringComparer.Ordinal);

        return new LandingPageDto(
            info.LandingBackgroundMode.ToString().ToLowerInvariant(),
            backgroundImageUrl,
            content);
    }

    private static string? ToAbsoluteUrl(string? baseUrl, string? storedUrl)
    {
        var joined = UrlJoin.Join(baseUrl, storedUrl);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }
}
