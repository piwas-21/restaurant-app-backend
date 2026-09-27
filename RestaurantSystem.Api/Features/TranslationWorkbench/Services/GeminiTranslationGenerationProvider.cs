using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class GeminiTranslationGenerationProvider(
    HttpClient client,
    IOptions<TranslationAssistanceSettings> options) : ITranslationGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TranslationGenerationResult> GenerateAsync(
        IReadOnlyList<TranslationGenerationTarget> targets,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var providerSettings = settings.Gemini;
        var endpoint = CreateEndpoint(providerSettings);
        if (endpoint is null || string.IsNullOrWhiteSpace(providerSettings.ApiKey))
        {
            throw new HttpRequestException("Translation provider is not configured");
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = TranslationGenerationPrompt.Instructions } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = JsonSerializer.Serialize(new { glossary, targets }, JsonOptions) } }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = settings.MaxOutputTokens,
                thinkingConfig = new { thinkingLevel = "low" },
                responseFormat = new
                {
                    text = new
                    {
                        mimeType = "application/json",
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                items = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            key = new { type = "string" },
                                            text = new { type = "string" }
                                        },
                                        required = new[] { "key", "text" },
                                        additionalProperties = false
                                    }
                                }
                            },
                            required = new[] { "items" },
                            additionalProperties = false
                        }
                    }
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Add("x-goog-api-key", providerSettings.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Translation provider rejected the request", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        return Parse(document.RootElement, targets, providerSettings.Model);
    }

    private static Uri? CreateEndpoint(GeminiTranslationProviderSettings settings)
    {
        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            string.IsNullOrWhiteSpace(settings.Model) || settings.Model.Contains('/'))
        {
            return null;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/models/{Uri.EscapeDataString(settings.Model)}:generateContent",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static TranslationGenerationResult Parse(
        JsonElement response,
        IReadOnlyList<TranslationGenerationTarget> targets,
        string model)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
        {
            throw new HttpRequestException("Translation provider returned no candidate");
        }

        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object ||
            !candidate.TryGetProperty("finishReason", out var finishReason) ||
            finishReason.ValueKind != JsonValueKind.String ||
            finishReason.GetString() != "STOP" ||
            !candidate.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            throw new HttpRequestException("Translation provider returned an incomplete response");
        }

        var textPart = parts.EnumerateArray()
            .FirstOrDefault(part => part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String);
        if (textPart.ValueKind != JsonValueKind.Object ||
            !textPart.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
        {
            throw new HttpRequestException("Translation provider returned no text");
        }

        var usage = response.TryGetProperty("usageMetadata", out var usageMetadata) ? usageMetadata : default;
        var inputTokens = ReadPositiveTokens(usage, "promptTokenCount");
        var outputTokens = ReadTotalOutputTokens(usage, inputTokens);
        return TranslationGenerationResponseParser.Parse(
            textValue.GetString()!, targets, "gemini", model, inputTokens, outputTokens);
    }

    private static int ReadPositiveTokens(JsonElement usage, string name)
    {
        if (usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty(name, out var value) || !value.TryGetInt32(out var tokens) || tokens <= 0)
        {
            throw new HttpRequestException("Translation provider returned invalid usage");
        }

        return tokens;
    }

    private static int ReadTotalOutputTokens(JsonElement usage, int inputTokens)
    {
        if (usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("candidatesTokenCount", out var candidatesValue) ||
            !candidatesValue.TryGetInt32(out var candidateTokens) || candidateTokens <= 0)
        {
            throw new HttpRequestException("Translation provider returned invalid usage");
        }

        if (usage.TryGetProperty("totalTokenCount", out var totalValue) &&
            totalValue.TryGetInt32(out var totalTokens) && totalTokens > inputTokens)
        {
            return totalTokens - inputTokens;
        }

        if (usage.TryGetProperty("thoughtsTokenCount", out var thoughtsValue) &&
            thoughtsValue.TryGetInt32(out var thoughtsTokens) && thoughtsTokens >= 0)
        {
            return checked(candidateTokens + thoughtsTokens);
        }

        throw new HttpRequestException("Translation provider returned invalid usage");
    }
}
