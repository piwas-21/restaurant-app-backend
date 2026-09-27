namespace RestaurantSystem.Api.Features.TranslationWorkbench;

public sealed class GeminiTranslationProviderSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3.8-flash";
    public decimal InputCostPerMillionUsd { get; set; }
    public decimal OutputCostPerMillionUsd { get; set; }
}
