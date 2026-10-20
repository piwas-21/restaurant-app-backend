using System.Text.Json.Serialization;

namespace Sofra.MobileService.Links;

public sealed record AppleAppSiteAssociationDto(AppleAppLinksDto Applinks);
public sealed record AppleAppLinksDto(IReadOnlyList<AppleAppDetailsDto> Details);
public sealed record AppleAppDetailsDto(
    [property: JsonPropertyName("appIDs")] IReadOnlyList<string> AppIds,
    IReadOnlyList<AppleLinkComponentDto> Components);
public sealed record AppleLinkComponentDto(
    [property: JsonPropertyName("/")] string Path,
    [property: JsonPropertyName("?")] IReadOnlyDictionary<string, string> Query);
