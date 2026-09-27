using RestaurantSystem.Api.Features.TranslationWorkbench.Services;

namespace RestaurantSystem.Api.Features.TranslationWorkbench;

public static class TranslationWorkbenchServiceCollectionExtensions
{
    public static IServiceCollection AddTranslationWorkbench(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TranslationAssistanceSettings>()
            .Bind(configuration.GetSection(TranslationAssistanceSettings.SectionName));
        services.AddScoped<ITranslationTextReader, TranslationTextReader>();
        services.AddScoped<ITranslationPreviewService, TranslationPreviewService>();
        services.AddScoped<ITranslationSuggestionService, TranslationSuggestionService>();
        services.AddScoped<ITranslationReviewService, TranslationReviewService>();
        services.AddScoped<ITranslationProvenanceWriter, TranslationProvenanceWriter>();
        services.AddHttpClient<ITranslationGenerationProvider, OpenAiTranslationGenerationProvider>();
        return services;
    }
}
