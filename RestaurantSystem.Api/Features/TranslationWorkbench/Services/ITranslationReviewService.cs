using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public interface ITranslationReviewService
{
    Task<TranslationReviewDto> ReviewAsync(
        TranslationReviewRequestDto request,
        CancellationToken cancellationToken);
}
