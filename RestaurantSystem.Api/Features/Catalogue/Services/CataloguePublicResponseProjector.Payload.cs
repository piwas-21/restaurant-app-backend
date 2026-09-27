using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CataloguePublicResponseProjector
{
    private static bool TryReadPayload(JsonElement source, string type, out JsonObject projected)
    {
        projected = new JsonObject();
        if (!source.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return type switch
        {
            "category" => TryReadCategoryPayload(payload, projected),
            "ingredient" => TryReadIngredientPayload(payload, projected),
            "option-set" => TryReadOptionSetPayload(payload, projected),
            "item" => TryReadItemPayload(payload, projected),
            "bundle" => TryReadBundlePayload(payload, projected),
            "cuisine-pack" => TryReadCuisinePackPayload(payload, projected),
            _ => false
        };
    }

    private static bool TryReadCategoryPayload(JsonElement payload, JsonObject projected)
    {
        if (!TryGetNonNegativeInt(payload, SortOrderProperty, out var order))
        {
            return false;
        }

        projected[SortOrderProperty] = order;
        return true;
    }

    private static bool TryReadIngredientPayload(JsonElement payload, JsonObject projected)
    {
        if (!TryGetBoolean(payload, "suggestedOnly", out var suggestedOnly) || !suggestedOnly ||
            !TryGetString(payload, "role", out var role))
        {
            return false;
        }

        projected["suggestedOnly"] = true;
        projected["role"] = role;
        return true;
    }

    private static bool TryReadOptionSetPayload(JsonElement payload, JsonObject projected)
    {
        if (!TryGetString(payload, "kind", out var kind) ||
            kind is not ("ingredient" or "sauce" or "bundle-option" or "suggested-side") ||
            !TryGetNonNegativeInt(payload, "min", out var min) ||
            !TryGetPositiveInt(payload, "max", out var max) || min > max ||
            !TryReadOptionReferences(payload, OptionsProperty, out var options))
        {
            return false;
        }

        projected["kind"] = kind;
        projected["min"] = min;
        projected["max"] = max;
        projected[OptionsProperty] = options;
        return true;
    }

    private static bool TryReadItemPayload(JsonElement payload, JsonObject projected)
    {
        if (!TryAddOptionalReference(payload, projected, "category") ||
            !TryReadReferences(payload, "suggestedIngredients", out var ingredients) ||
            !TryReadReferences(payload, "optionSets", out var sets) ||
            !TryReadReferences(payload, "sideSets", out var sides) ||
            !TryReadReviewFields(payload, projected))
        {
            return false;
        }

        projected["suggestedIngredients"] = ingredients;
        projected["optionSets"] = sets;
        projected["sideSets"] = sides;
        return true;
    }

    private static bool TryReadBundlePayload(JsonElement payload, JsonObject projected)
    {
        if (!TryAddOptionalReference(payload, projected, "standaloneOffer") ||
            !TryAddOptionalReference(payload, projected, "offerFamily") ||
            !TryReadBundleSections(payload, out var sections) ||
            !TryReadReviewFields(payload, projected))
        {
            return false;
        }

        projected["sections"] = sections;
        return true;
    }

    private static bool TryReadCuisinePackPayload(JsonElement payload, JsonObject projected)
    {
        if (!TryReadPackReferences(payload, "categories", false, out var categories) ||
            !TryReadPackReferences(payload, "offers", true, out var offers) ||
            !TryReadReviewFields(payload, projected))
        {
            return false;
        }

        projected["categories"] = categories;
        projected["offers"] = offers;
        return true;
    }

    private static bool TryReadBundleSections(JsonElement payload, out JsonArray projected)
    {
        projected = new JsonArray();
        if (!payload.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var section in sections.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Object ||
                !TryGetString(section, "sectionKey", out var key) || !TemplateId().IsMatch(key) ||
                !TryGetString(section, "name", out var name) ||
                !TryGetNonNegativeInt(section, SortOrderProperty, out var order) ||
                !TryGetNonNegativeInt(section, "min", out var min) ||
                !TryGetPositiveInt(section, "max", out var max) || min > max ||
                !TryReadOptionReferences(section, OptionsProperty, out var options)) return false;
            var item = new JsonObject
            {
                ["sectionKey"] = key,
                ["name"] = name,
                [SortOrderProperty] = order,
                ["min"] = min,
                ["max"] = max,
                [OptionsProperty] = options
            };
            if (section.TryGetProperty("translations", out var translations))
            {
                if (translations.ValueKind != JsonValueKind.Object ||
                    !TryReadLocaleMap(section, "translations", out var projectedTranslations)) return false;
                item["translations"] = projectedTranslations;
            }
            projected.Add(item);
        }
        return true;
    }

    private static bool TryReadPackReferences(
        JsonElement payload,
        string property,
        bool requireDefault,
        out JsonArray projected)
    {
        projected = new JsonArray();
        if (!payload.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object || !TryReadReference(value, out var reference) ||
                !TryGetNonNegativeInt(value, SortOrderProperty, out var sortOrder)) return false;
            reference[SortOrderProperty] = sortOrder;
            if (value.TryGetProperty("includedByDefault", out var defaultValue))
            {
                if (defaultValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                reference["includedByDefault"] = defaultValue.GetBoolean();
            }
            else if (requireDefault)
            {
                return false;
            }
            projected.Add(reference);
        }
        return true;
    }

    private static bool TryReadOptionReferences(JsonElement payload, string property, out JsonArray projected)
    {
        projected = new JsonArray();
        if (!payload.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        var orders = new HashSet<int>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object || !TryReadReference(value, out var reference) ||
                !TryGetNonNegativeInt(value, SortOrderProperty, out var sortOrder) || !orders.Add(sortOrder)) return false;
            reference[SortOrderProperty] = sortOrder;
            if (value.TryGetProperty("default", out var defaultValue))
            {
                if (defaultValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                reference["default"] = defaultValue.GetBoolean();
            }
            projected.Add(reference);
        }
        return true;
    }

    private static bool TryReadReferences(JsonElement payload, string property, out JsonArray projected)
    {
        projected = new JsonArray();
        if (!payload.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var value in values.EnumerateArray())
        {
            if (!TryReadReference(value, out var reference)) return false;
            projected.Add(reference);
        }
        return true;
    }

    private static bool TryAddOptionalReference(JsonElement payload, JsonObject projected, string property)
    {
        if (!payload.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (!TryReadReference(value, out var reference)) return false;
        projected[property] = reference;
        return true;
    }

    private static bool TryReadReference(JsonElement source, out JsonObject projected)
    {
        projected = new JsonObject();
        if (source.ValueKind != JsonValueKind.Object ||
            !TryGetString(source, "templateId", out var templateId) || !TemplateId().IsMatch(templateId) ||
            !TryGetPositiveInt(source, "revision", out var revision)) return false;
        projected = new JsonObject { ["templateId"] = templateId, ["revision"] = revision };
        return true;
    }

    private static bool TryReadReviewFields(JsonElement payload, JsonObject projected)
    {
        if (!payload.TryGetProperty("requiredLocalReviewFields", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.String || !ReviewFields.Contains(field.GetString()!) ||
                !seen.Add(field.GetString()!)) return false;
            result.Add(field.GetString());
        }
        projected["requiredLocalReviewFields"] = result;
        return true;
    }

}
