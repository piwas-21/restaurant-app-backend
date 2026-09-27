using System.Text.Json;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CatalogueImportPayloadReader
{
    private static readonly HashSet<string> SupportedLocales =
        ["en", "fr", "de", "nl", "tr", "ar", "es", "it", "ru", "zh"];

    public static CatalogueSourceReference? ReadOptionalReference(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadReference(value, property);
    }

    public static IReadOnlyList<CatalogueSourceReference> ReadReferences(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw Unsupported($"The {property} reference list is missing or invalid.");
        }

        var references = value.EnumerateArray().Select(element => ReadReference(element, property)).ToArray();
        if (references.Select(reference => reference.Key).Distinct(StringComparer.Ordinal).Count() != references.Length)
        {
            throw Unsupported($"The {property} list contains duplicate references.");
        }

        return references;
    }

    public static CatalogueOptionSetPayload ReadOptionSet(CentralCatalogueTemplateRevision revision)
    {
        var payload = RequirePayload(revision, "option-set");
        var kind = ReadString(payload, "kind");
        if (kind is not ("ingredient" or "sauce" or "bundle-option" or "suggested-side"))
        {
            throw Unsupported("This option-set kind is not supported.");
        }

        var options = CatalogueOptionReferenceReader.ReadOptionReferences(payload, "options");
        var minimum = ReadInt(payload, "min");
        var maximum = ReadInt(payload, "max");
        ValidateCardinality(minimum, maximum, options, "option set");
        return new CatalogueOptionSetPayload(kind, minimum, maximum, options);
    }

    public static string ReadIngredientRole(CentralCatalogueTemplateRevision revision)
    {
        var payload = RequirePayload(revision, "ingredient");
        if (ReadBool(payload, "suggestedOnly") != true)
        {
            throw Unsupported("Ingredient templates must be explicitly marked as suggestions.");
        }

        return ReadString(payload, "role");
    }

    public static IReadOnlyList<CatalogueBundleSection> ReadBundleSections(
        CentralCatalogueTemplateRevision revision)
    {
        var payload = RequirePayload(revision, "bundle");
        if (!payload.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array ||
            sections.GetArrayLength() == 0)
        {
            throw Unsupported("This bundle has no supported sections.");
        }

        var parsed = sections.EnumerateArray().Select(ReadBundleSection).ToArray();
        if (parsed.Select(section => section.Key).Distinct(StringComparer.Ordinal).Count() != parsed.Length ||
            parsed.Select(section => section.DisplayOrder).Distinct().Count() != parsed.Length)
        {
            throw Unsupported("This bundle has duplicate section keys or display orders.");
        }

        return parsed.OrderBy(section => section.DisplayOrder).ToArray();
    }

    public static bool HasUnsupportedOfferFamily(CentralCatalogueTemplateRevision revision) =>
        revision.Type == "bundle" && revision.Payload.ValueKind == JsonValueKind.Object &&
        revision.Payload.TryGetProperty("offerFamily", out var value) && value.ValueKind != JsonValueKind.Null;

    public static void EnsureSupportedPayload(CentralCatalogueTemplateRevision revision)
    {
        if (revision.Payload.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported("The template payload is not an object.");
        }

        if (revision.Payload.TryGetProperty("variations", out var variations) && variations.ValueKind != JsonValueKind.Null)
        {
            throw Unsupported("Variation references are not supported by this importer.", "UNSUPPORTED_VARIATION_REFERENCE");
        }

        if (HasUnsupportedOfferFamily(revision))
        {
            throw Unsupported("Offer-family references are not supported yet.", "UNSUPPORTED_OFFER_FAMILY");
        }

        switch (revision.Type)
        {
            case "category":
                _ = ReadInt(revision.Payload, "sortOrder");
                break;
            case "ingredient":
                if (ReadBool(revision.Payload, "suggestedOnly") != true ||
                    string.IsNullOrWhiteSpace(ReadString(revision.Payload, "role")))
                {
                    throw Unsupported("Ingredient templates must be explicitly marked as suggestions.");
                }
                break;
            case "item":
                _ = ReadOptionalReference(revision.Payload, "category");
                _ = ReadReferences(revision.Payload, "suggestedIngredients");
                _ = ReadReferences(revision.Payload, "optionSets");
                _ = ReadReferences(revision.Payload, "sideSets");
                break;
            case "option-set":
                _ = ReadOptionSet(revision);
                break;
            case "bundle":
                _ = ReadOptionalReference(revision.Payload, "standaloneOffer");
                _ = ReadBundleSections(revision);
                break;
            case "cuisine-pack":
                _ = ReadPackReferences(revision.Payload, "categories");
                _ = ReadPackReferences(revision.Payload, "offers");
                break;
            default:
                throw Unsupported("This template type is not supported by the importer.");
        }
    }

    public static IReadOnlyList<CataloguePackReference> ReadPackReferences(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw Unsupported($"The cuisine pack {property} list is missing or invalid.");
        }

        return values.EnumerateArray().Select(value => new CataloguePackReference(
            ReadReference(value, property),
            ReadInt(value, "sortOrder"),
            ReadPackDefault(value, property)))
            .OrderBy(value => value.SortOrder)
            .ToArray();
    }

    private static bool ReadPackDefault(JsonElement value, string property)
    {
        if (property == "categories")
        {
            return false;
        }

        return value.TryGetProperty("includedByDefault", out var included) &&
            included.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? included.GetBoolean()
                : throw Unsupported("A cuisine-pack offer must specify whether it is selected by default.");
    }

    private static CatalogueBundleSection ReadBundleSection(JsonElement section)
    {
        var key = ReadString(section, "sectionKey");
        var name = ReadString(section, "name");
        var sortOrder = ReadInt(section, "sortOrder");
        var minimum = ReadInt(section, "min");
        var maximum = ReadInt(section, "max");
        var options = CatalogueOptionReferenceReader.ReadOptionReferences(section, "options");
        ValidateCardinality(minimum, maximum, options, "bundle section");
        if (!SectionKeyPattern().IsMatch(key) || name.Length > 120 || sortOrder < 0)
        {
            throw Unsupported("A bundle section has an invalid key, name, or order.");
        }

        return new CatalogueBundleSection(key, name, sortOrder, minimum, maximum, options,
            ReadSectionTranslations(section));
    }

    private static Dictionary<string, string> ReadSectionTranslations(JsonElement section)
    {
        if (!section.TryGetProperty("translations", out var translations) || translations.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (translations.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported("Bundle section translations must be a locale map.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in translations.EnumerateObject())
        {
            if (!SupportedLocales.Contains(locale.Name) || locale.Value.ValueKind != JsonValueKind.Object)
            {
                throw Unsupported("A bundle section translation has an unsupported locale or shape.");
            }

            var name = ReadString(locale.Value, "name");
            if (name.Length > 120)
            {
                throw Unsupported("A bundle section translation is too long.");
            }

            result.Add(locale.Name, name);
        }

        return result;
    }

    private static void ValidateCardinality(
        int minimum,
        int maximum,
        CatalogueOptionReference[] options,
        string description)
    {
        var defaultCount = options.Count(option => option.IsDefault);
        if (minimum < 0 || maximum <= 0 || minimum > maximum || maximum > options.Length ||
            defaultCount > 0 && (defaultCount < minimum || defaultCount > maximum))
        {
            var code = description == "bundle section"
                ? "UNSUPPORTED_BUNDLE_CARDINALITY"
                : "UNSUPPORTED_OPTION_SET_CARDINALITY";
            throw Unsupported($"The {description} has unsupported selection bounds or defaults.", code);
        }
    }

    private static JsonElement RequirePayload(CentralCatalogueTemplateRevision revision, string expectedType)
    {
        if (revision.Type != expectedType || revision.Payload.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported($"The {expectedType} payload is invalid.");
        }

        return revision.Payload;
    }

    internal static CatalogueSourceReference ReadReference(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("templateId", out var id) || id.ValueKind != JsonValueKind.String ||
            !TemplateIdPattern().IsMatch(id.GetString() ?? string.Empty) ||
            !value.TryGetProperty("revision", out var revision) || !revision.TryGetInt32(out var number) || number < 1)
        {
            throw Unsupported($"A {description} reference is invalid.");
        }

        return new CatalogueSourceReference(id.GetString()!, number);
    }

    internal static int ReadInt(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.TryGetInt32(out var number) && number >= 0
            ? number
            : throw Unsupported($"The {property} value is invalid.");

    private static string ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : throw Unsupported($"The {property} value is invalid.");

    private static bool? ReadBool(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element)
            ? element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    internal static BadRequestException Unsupported(
        string message,
        string errorCode = "UNSUPPORTED_SOURCE_PAYLOAD") => new(message, errorCode);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdPattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionKeyPattern();
}
