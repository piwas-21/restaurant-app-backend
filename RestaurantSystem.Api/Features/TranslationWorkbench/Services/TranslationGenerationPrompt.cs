namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

internal static class TranslationGenerationPrompt
{
    public const string Instructions =
        "Translate only the requested restaurant menu text. Preserve every number, currency, and placeholder exactly. Do not add ingredients, allergens, claims, or prices. Use the glossary and exclusion context as supplied. Return one text for each key.";
}
