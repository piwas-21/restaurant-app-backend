using System.Text.Json;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

internal static class TranslationGenerationResponseParser
{
    public static TranslationGenerationResult Parse(
        string text,
        IReadOnlyList<TranslationGenerationTarget> targets,
        string provider,
        string model,
        int inputTokens,
        int outputTokens)
    {
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Translation provider returned invalid JSON", exception);
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                parsed.RootElement.EnumerateObject().Count() != 1)
            {
                throw new HttpRequestException("Translation provider returned an invalid batch");
            }

            var targetByKey = targets.ToDictionary(target => target.Key, StringComparer.Ordinal);
            if (!parsed.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != targetByKey.Count)
            {
                throw new HttpRequestException("Translation provider returned an invalid batch");
            }

            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 ||
                    !item.TryGetProperty("key", out var keyValue) || keyValue.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("text", out var translated) || translated.ValueKind != JsonValueKind.String)
                {
                    throw new HttpRequestException("Translation provider returned an invalid item");
                }

                var key = keyValue.GetString()!;
                var value = translated.GetString()!;
                if (!targetByKey.TryGetValue(key, out var target) || !results.TryAdd(key, value) ||
                    !TranslationWorkbenchRules.IsSafeSuggestion(target.SourceText, value, target.FieldKey))
                {
                    throw new HttpRequestException("Translation provider returned unsafe text");
                }
            }

            if (results.Count != targetByKey.Count)
            {
                throw new HttpRequestException("Translation provider returned an incomplete batch");
            }

            return new TranslationGenerationResult(results, provider, model, inputTokens, outputTokens);
        }
    }
}
