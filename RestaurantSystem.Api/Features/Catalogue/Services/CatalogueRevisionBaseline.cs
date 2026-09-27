using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueRevisionBaseline
{
    private const string BundleType = "bundle";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(CentralCatalogueTemplateRevision revision, string templateType) =>
        Serialize(Fields(revision, templateType).ToDictionary(
            pair => pair.Key,
            pair => new CatalogueRevisionBaselineField(pair.Value, revision.Revision, revision.ContentHash),
            StringComparer.Ordinal));

    public static Dictionary<string, CatalogueRevisionBaselineField> Read(
        string? json,
        string templateType,
        int adoptedRevision,
        string adoptedContentHash)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<CatalogueRevisionBaselineDocument>(json, JsonOptions)?.FieldValues ?? [];
        }

        var legacy = JsonSerializer.Deserialize<CentralCatalogueTemplateRevision>(json, JsonOptions);
        return legacy is null
            ? []
            : Fields(legacy, templateType).ToDictionary(
                pair => pair.Key,
                pair => new CatalogueRevisionBaselineField(pair.Value, adoptedRevision, adoptedContentHash),
                StringComparer.Ordinal);
    }

    public static string Advance(
        string? priorJson,
        CentralCatalogueTemplateRevision revision,
        string templateType,
        IReadOnlyCollection<string> selectedPaths,
        int priorRevision,
        string priorContentHash)
    {
        var fields = Read(priorJson, templateType, priorRevision, priorContentHash);
        var current = Fields(revision, templateType);
        foreach (var path in selectedPaths)
        {
            current.TryGetValue(path, out var value);
            fields[path] = new CatalogueRevisionBaselineField(value, revision.Revision, revision.ContentHash);
        }

        return Serialize(fields);
    }

    public static Dictionary<string, string?> Fields(CentralCatalogueTemplateRevision revision, string templateType)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (templateType is "category" or "item" or BundleType or "option-set")
        {
            fields["name"] = revision.Name;
        }

        if (templateType is "category" or "item" or BundleType)
        {
            fields["description"] = revision.Description;
        }

        if (templateType is "ingredient" or "item" or BundleType or "option-set")
        {
            foreach (var (locale, translation) in revision.Translations)
            {
                fields[$"translations[{locale}].name"] = translation.Name;
                if (templateType is "item" or BundleType)
                {
                    fields[$"translations[{locale}].description"] = translation.Description;
                }
            }
        }

        if (templateType == BundleType)
        {
            foreach (var section in CatalogueImportPayloadReader.ReadBundleSections(revision))
            {
                fields[$"sections[{section.Key}].name"] = section.Name;
                foreach (var (locale, name) in section.Translations)
                {
                    fields[$"sections[{section.Key}].translations[{locale}].name"] = name;
                }
            }
        }

        return fields;
    }

    public static string ComputeLocalHash(IReadOnlyDictionary<string, string?> fields)
    {
        var canonical = fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CatalogueRevisionLocalField(pair.Key, pair.Value))
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions)))
            .ToLowerInvariant();
    }

    private static string Serialize(Dictionary<string, CatalogueRevisionBaselineField> fields) =>
        JsonSerializer.Serialize(new CatalogueRevisionBaselineDocument(1, fields), JsonOptions);

    internal sealed record CatalogueRevisionBaselineDocument(
        int SchemaVersion,
        [property: System.Text.Json.Serialization.JsonPropertyName("fields")]
        Dictionary<string, CatalogueRevisionBaselineField> FieldValues);

    internal sealed record CatalogueRevisionBaselineField(string? Value, int SourceRevision, string ContentHash);

    private sealed record CatalogueRevisionLocalField(string Path, string? Value);
}
