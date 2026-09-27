using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public interface ITranslationPreviewService
{
    Task<TranslationPreviewDto> PreviewAsync(
        TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken);
}
