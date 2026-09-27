using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Api.Common.Conventers;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializationJobJson
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new JsonException("The saved option-set job data is empty.");

    public static OptionSetMaterializationRequest WithTargets(
        OptionSetMaterializationRequest request,
        IEnumerable<int> targetSequences)
    {
        var targets = targetSequences.Select(sequence =>
        {
            if (sequence < 0 || sequence >= request.Targets.Count)
            {
                throw new JsonException("A saved option-set job target is outside its immutable request.");
            }

            return request.Targets[sequence];
        }).ToArray();
        if (targets.Length == 0)
        {
            throw new JsonException("The saved option-set job has no pending targets to validate.");
        }

        return new OptionSetMaterializationRequest
        {
            OptionSetId = request.OptionSetId,
            ExpectedSetVersion = request.ExpectedSetVersion,
            IdempotencyKey = request.IdempotencyKey,
            Targets = targets
        };
    }

    public static string Fingerprint(OptionSetMaterializationRequest request)
    {
        using var document = JsonDocument.Parse(Serialize(request));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StringEnumConverterFactory());
        return options;
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
