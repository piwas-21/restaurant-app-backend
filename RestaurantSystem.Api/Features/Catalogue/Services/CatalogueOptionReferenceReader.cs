using System.Text.Json;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueOptionReferenceReader
{
    public static CatalogueOptionReference[] ReadOptionReferences(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() == 0)
        {
            throw CatalogueImportPayloadReader.Unsupported($"The {property} option list is missing or empty.");
        }

        var options = values.EnumerateArray().Select(value => new CatalogueOptionReference(
            CatalogueImportPayloadReader.ReadReference(value, property),
            CatalogueImportPayloadReader.ReadInt(value, "sortOrder"),
            ReadOptionalBoolean(value, "default", false))).ToArray();
        if (options.Any(option => option.SortOrder < 0) ||
            options.Select(option => option.Reference.Key).Distinct(StringComparer.Ordinal).Count() != options.Length)
        {
            throw CatalogueImportPayloadReader.Unsupported("An option list has duplicate references or invalid ordering.");
        }

        if (options.Select(option => option.SortOrder).Distinct().Count() != options.Length)
        {
            throw CatalogueImportPayloadReader.Unsupported(
                "An option list contains duplicate sort orders.", "UNSUPPORTED_OPTION_ORDERING");
        }

        return options.OrderBy(option => option.SortOrder).ToArray();
    }

    private static bool ReadOptionalBoolean(JsonElement value, string property, bool defaultValue)
    {
        if (!value.TryGetProperty(property, out var element))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw CatalogueImportPayloadReader.Unsupported(
                $"The {property} value must be a boolean.", "UNSUPPORTED_OPTION_DEFAULT")
        };
    }
}
