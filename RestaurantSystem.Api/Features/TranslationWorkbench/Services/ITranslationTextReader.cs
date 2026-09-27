using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public interface ITranslationTextReader
{
    Task<IReadOnlyDictionary<string, string>> ReadAsync(
        TranslationFieldInputDto input,
        CancellationToken cancellationToken);
}
