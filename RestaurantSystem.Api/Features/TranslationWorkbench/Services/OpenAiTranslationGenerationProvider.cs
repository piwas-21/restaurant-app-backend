using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class OpenAiTranslationGenerationProvider(
    HttpClient client,
    IOptions<TranslationAssistanceSettings> options) : ITranslationGenerationProvider
{
    private const string Instructions = "Translate only the requested restaurant menu text. Preserve every number, currency, and placeholder exactly. Do not add ingredients, allergens, claims, or prices. Use the glossary and exclusion context as supplied. Return one text for each key.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TranslationGenerationResult> GenerateAsync(
        IReadOnlyList<TranslationGenerationTarget> targets,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new HttpRequestException("Translation provider is not configured");
        }

        var body = new
        {
            model = settings.Model,
            store = false,
            reasoning = new { effort = "none" },
            max_output_tokens = settings.MaxOutputTokens,
            instructions = Instructions,
            input = JsonSerializer.Serialize(new { glossary, targets }, JsonOptions),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "menu_translation_batch",
                    strict = true,
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
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Translation provider rejected the request", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        return Parse(document.RootElement, targets, settings.Model);
    }

    private static TranslationGenerationResult Parse(
        JsonElement response,
        IReadOnlyList<TranslationGenerationTarget> targets,
        string model)
    {
        if (!response.TryGetProperty("status", out var status) || status.GetString() != "completed" ||
            !response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw new HttpRequestException("Translation provider returned an incomplete response");
        }

        var text = output.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var kind) && kind.GetString() == "message")
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .FirstOrDefault(item => item.TryGetProperty("type", out var kind) && kind.GetString() == "output_text");
        if (text.ValueKind != JsonValueKind.Object ||
            !text.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
        {
            throw new HttpRequestException("Translation provider returned no text");
        }

        using var parsed = JsonDocument.Parse(textValue.GetString()!);
        if (!parsed.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != targets.Count)
        {
            throw new HttpRequestException("Translation provider returned an invalid batch");
        }

        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("key", out var keyValue) || keyValue.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("text", out var translated) || translated.ValueKind != JsonValueKind.String)
            {
                throw new HttpRequestException("Translation provider returned an invalid item");
            }

            var key = keyValue.GetString()!;
            var target = targets.FirstOrDefault(candidate => candidate.Key == key);
            var value = translated.GetString()!;
            if (target is null || !results.TryAdd(key, value) ||
                !TranslationWorkbenchRules.IsSafeSuggestion(target.SourceText, value, target.FieldKey))
            {
                throw new HttpRequestException("Translation provider returned unsafe text");
            }
        }

        var usage = response.TryGetProperty("usage", out var usageValue) ? usageValue : default;
        var inputTokens = ReadTokens(usage, "input_tokens");
        var outputTokens = ReadTokens(usage, "output_tokens");
        return new TranslationGenerationResult(results, "openai", model, inputTokens, outputTokens);
    }

    private static int ReadTokens(JsonElement usage, string key) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(key, out var value) &&
        value.TryGetInt32(out var count) ? count : 0;
}
