using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CataloguePublicResponseProjector
{
    private static bool TryReadStringArray(
        JsonElement source,
        string property,
        out JsonArray projected,
        bool slugValues = false)
    {
        projected = new JsonArray();
        if (!source.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String ||
                (slugValues && !TemplateId().IsMatch(value.GetString()!))) return false;
            projected.Add(value.GetString());
        }
        return true;
    }

    private static bool TryReadLocaleArray(JsonElement source, string property, out JsonArray projected)
    {
        if (!TryReadStringArray(source, property, out projected)) return false;
        return projected.All(node => node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var locale) && IsLocale(locale));
    }

    private static bool TryReadPositiveIntArray(JsonElement source, string property, out JsonArray projected)
    {
        projected = new JsonArray();
        if (!source.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 1)
            {
                return false;
            }
            projected.Add(number);
        }
        return true;
    }

    private static bool TryGetString(JsonElement source, string property, out string value)
    {
        value = string.Empty;
        if (!source.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetNullableString(
        JsonElement source,
        string property,
        out string? value,
        bool required = true)
    {
        value = null;
        if (!source.TryGetProperty(property, out var element)) return !required;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool TryGetPositiveInt(JsonElement source, string property, out int value)
    {
        value = 0;
        return source.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value > 0;
    }

    private static bool TryGetNonNegativeInt(JsonElement source, string property, out int value)
    {
        value = 0;
        return source.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value >= 0;
    }

    private static bool TryGetBoolean(JsonElement source, string property, out bool value)
    {
        value = false;
        if (!source.TryGetProperty(property, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    private static void AddOptionalString(JsonElement source, JsonObject target, string property)
    {
        if (source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            target[property] = value.GetString();
        }
    }

    private static bool IsLocale(string locale) => LocalePattern().IsMatch(locale);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateId();

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}
