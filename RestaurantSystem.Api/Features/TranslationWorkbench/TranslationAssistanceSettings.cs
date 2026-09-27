namespace RestaurantSystem.Api.Features.TranslationWorkbench;

public sealed class TranslationAssistanceSettings
{
    public const string SectionName = "TranslationAssistance";
    private const string GeminiProvider = "gemini";
    public bool Enabled { get; set; }
    public bool TenantDataApproved { get; set; }
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-6-luna";
    public GeminiTranslationProviderSettings Gemini { get; set; } = new();
    public string PromptVersion { get; set; } = "1";
    public int MaxOutputTokens { get; set; } = 2048;
    public int MaxDailyBatches { get; set; } = 40;
    public int MaxDailyTokens { get; set; } = 80000;
    public decimal MaxDailySpendUsd { get; set; } = 2m;
    public decimal InputCostPerMillionUsd { get; set; } = 0.10m;
    public decimal OutputCostPerMillionUsd { get; set; } = 0.50m;
    public int MaxBatchTargets { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 20;
    public Dictionary<string, string> Glossary { get; set; } = new(StringComparer.Ordinal);

    public string SelectedModel => Provider == GeminiProvider ? Gemini.Model : Model;
    public string ContextModelKey => Provider == "openai" ? SelectedModel : $"{Provider}:{SelectedModel}";
    public decimal SelectedInputCostPerMillionUsd => Provider == GeminiProvider ? Gemini.InputCostPerMillionUsd : InputCostPerMillionUsd;
    public decimal SelectedOutputCostPerMillionUsd => Provider == GeminiProvider ? Gemini.OutputCostPerMillionUsd : OutputCostPerMillionUsd;
    public bool CanGenerate => Enabled && TenantDataApproved && HasProviderConfiguration;

    public bool HasProviderConfiguration => Provider switch
    {
        "openai" => !string.IsNullOrWhiteSpace(ApiKey) && IsHttpsUrl(ApiUrl),
        GeminiProvider => !string.IsNullOrWhiteSpace(Gemini.ApiKey) &&
            IsHttpsBaseUrl(Gemini.ApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(Gemini.Model) && !Gemini.Model.Contains('/') &&
            Gemini.InputCostPerMillionUsd > 0 && Gemini.OutputCostPerMillionUsd > 0,
        _ => false
    };

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsHttpsBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}
