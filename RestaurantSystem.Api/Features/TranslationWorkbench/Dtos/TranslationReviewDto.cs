namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

public sealed record TranslationDecisionDto(Guid SuggestionId, string Decision, string? Text);
public sealed record TranslationReviewRequestDto(List<TranslationDecisionDto> Decisions);
public sealed record TranslationDecisionResultDto(Guid SuggestionId, string Decision, string Status, string? Text);
public sealed record TranslationReviewDto(IReadOnlyList<TranslationDecisionResultDto> Decisions);
