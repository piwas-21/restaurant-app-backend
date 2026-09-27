using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using Microsoft.Extensions.Options;

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
        services.AddHttpClient<OpenAiTranslationGenerationProvider>();
        services.AddHttpClient<GeminiTranslationGenerationProvider>();
        services.AddScoped<ITranslationGenerationProvider>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TranslationAssistanceSettings>>().Value;
            return settings.Provider switch
            {
                "openai" => serviceProvider.GetRequiredService<OpenAiTranslationGenerationProvider>(),
                "gemini" => serviceProvider.GetRequiredService<GeminiTranslationGenerationProvider>(),
                _ => new UnconfiguredTranslationGenerationProvider()
            };
        });
        return services;
    }

    private sealed class UnconfiguredTranslationGenerationProvider : ITranslationGenerationProvider
    {
        public Task<TranslationGenerationResult> GenerateAsync(
            IReadOnlyList<TranslationGenerationTarget> targets,
            IReadOnlyDictionary<string, string> glossary,
            CancellationToken cancellationToken) =>
            Task.FromException<TranslationGenerationResult>(
                new HttpRequestException("Translation provider is not configured"));
    }
}
