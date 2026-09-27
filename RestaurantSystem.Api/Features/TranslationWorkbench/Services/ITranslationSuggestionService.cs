using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public interface ITranslationSuggestionService
{
    Task<TranslationSuggestionsDto> SuggestAsync(
        TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken);
}
