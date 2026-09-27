namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

/// <summary>Submitted with ordinary menu text; accepted IDs are verified before provenance is stored.</summary>
public sealed record TranslationOwnerMetadataDto
{
    public Dictionary<string, string> SourceLocales { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> AcceptedSuggestionIds { get; init; } = new(StringComparer.Ordinal);
}
