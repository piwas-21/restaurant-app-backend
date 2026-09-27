using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CataloguePublicResponseProjector
{
    private const string CuisinesProperty = "cuisines";
    private const string CompatibleTenantContractVersionsProperty = "compatibleTenantContractVersions";
    private const string SourceLocaleProperty = "sourceLocale";
    private const string DescriptionProperty = "description";
    private const string LicenseProperty = "license";
    private const string SortOrderProperty = "sortOrder";
    private const string OptionsProperty = "options";

    private static readonly HashSet<string> Types =
        ["ingredient", "option-set", "item", "bundle", "category", "cuisine-pack"];
    private static readonly HashSet<string> ReviewFields =
        ["price", "ingredients", "allergens", "availability", "channels", "kitchen-routing", "images"];
    private static readonly HashSet<string> ProvenanceOrigins =
        ["sofra-original", "external-licensed", "public-domain"];
    private static readonly HashSet<string> DependencyRoles =
        ["category", "offer", "ingredient", "option-set", "side-set", "bundle-option", "offer-family"];

    public static bool TryProjectPage(JsonElement source, out JsonElement projected)
    {
        projected = default;
        if (source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() > 100 || !source.TryGetProperty("nextCursor", out var nextCursor) ||
            nextCursor.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return false;
        }

        var publicItems = new JsonArray();
        foreach (var item in items.EnumerateArray())
        {
            if (!TryProjectSummary(item, out var publicItem))
            {
                return false;
            }
            publicItems.Add(publicItem);
        }

        if (nextCursor.ValueKind == JsonValueKind.String && nextCursor.GetString()!.Length > 512)
        {
            return false;
        }

        var page = new JsonObject
        {
            ["items"] = publicItems,
            ["nextCursor"] = nextCursor.ValueKind == JsonValueKind.Null ? null : nextCursor.GetString()
        };
        projected = JsonSerializer.SerializeToElement(page);
        return true;
    }

    public static bool TryProjectRevision(
        JsonElement source,
        string requestedTemplateId,
        int requestedRevision,
        out JsonElement projected)
    {
        projected = default;
        if (source.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        CentralCatalogueTemplateRevision revision;
        try
        {
            revision = CatalogueTemplateGraphLoader.Deserialize(source);
            CatalogueTemplateGraphLoader.ValidateRevisionDocument(revision, requestedTemplateId, requestedRevision);
        }
        catch (Exception exception) when (exception is JsonException or BadRequestException)
        {
            return false;
        }

        if (!Types.Contains(revision.Type) ||
            !TryReadStringArray(source, CuisinesProperty, out var cuisines, slugValues: true) ||
            !TryReadLocaleMap(source, "translations", out var translations) ||
            !TryReadLocaleArray(source, "localeFallbacks", out var fallbacks) ||
            !TryReadDependencies(source, out var dependencies) ||
            !TryReadProvenance(source, out var provenance) ||
            !TryReadPayload(source, revision.Type, out var payload) ||
            !TryReadPositiveIntArray(source, CompatibleTenantContractVersionsProperty, out var contractVersions) ||
            !TryGetString(source, SourceLocaleProperty, out var sourceLocale) || !IsLocale(sourceLocale) ||
            !TryGetString(source, "name", out var name) || string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            !TryGetNullableString(source, DescriptionProperty, out var description) ||
            !TryGetString(source, "contentHash", out var contentHash) ||
            string.IsNullOrWhiteSpace(contentHash) || contentHash.Length > 128)
        {
            return false;
        }

        var publicRevision = new JsonObject
        {
            ["schemaVersion"] = revision.SchemaVersion,
            ["templateId"] = revision.TemplateId,
            ["revision"] = revision.Revision,
            ["type"] = revision.Type,
            [CuisinesProperty] = cuisines,
            ["name"] = name,
            [DescriptionProperty] = description,
            [SourceLocaleProperty] = sourceLocale,
            ["translations"] = translations,
            ["localeFallbacks"] = fallbacks,
            ["dependencies"] = dependencies,
            ["provenance"] = provenance,
            ["qualityStatus"] = "reviewed",
            [CompatibleTenantContractVersionsProperty] = contractVersions,
            ["payload"] = payload,
            ["contentHash"] = contentHash
        };
        projected = JsonSerializer.SerializeToElement(publicRevision);
        return true;
    }

    private static bool TryProjectSummary(JsonElement source, out JsonObject projected)
    {
        projected = new JsonObject();
        if (source.ValueKind != JsonValueKind.Object ||
            !TryGetString(source, "templateId", out var templateId) || !TemplateId().IsMatch(templateId) ||
            !TryGetPositiveInt(source, "revision", out var revision) ||
            !TryGetString(source, "type", out var type) || !Types.Contains(type) ||
            !TryReadStringArray(source, CuisinesProperty, out var cuisines, slugValues: true) ||
            !TryGetString(source, "displayName", out var displayName) ||
            string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200 ||
            !TryGetString(source, SourceLocaleProperty, out var sourceLocale) || !IsLocale(sourceLocale) ||
            !TryGetString(source, "displayLocale", out var displayLocale) || !IsLocale(displayLocale) ||
            !TryGetBoolean(source, "usedSourceFallback", out var usedSourceFallback) ||
            !TryReadLocaleArray(source, "reviewedTranslationLocales", out var reviewedLocales) ||
            !TryGetNonNegativeInt(source, "dependencyCount", out var dependencyCount) ||
            !TryReadPositiveIntArray(source, CompatibleTenantContractVersionsProperty, out var contractVersions))
        {
            return false;
        }

        projected = new JsonObject
        {
            ["templateId"] = templateId,
            ["revision"] = revision,
            ["type"] = type,
            [CuisinesProperty] = cuisines,
            ["displayName"] = displayName,
            [SourceLocaleProperty] = sourceLocale,
            ["displayLocale"] = displayLocale,
            ["usedSourceFallback"] = usedSourceFallback,
            ["reviewedTranslationLocales"] = reviewedLocales,
            ["dependencyCount"] = dependencyCount,
            [CompatibleTenantContractVersionsProperty] = contractVersions
        };
        return true;
    }

    private static bool TryReadLocaleMap(JsonElement source, string property, out JsonObject projected)
    {
        projected = new JsonObject();
        if (!source.TryGetProperty(property, out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var locale in map.EnumerateObject())
        {
            if (!IsLocale(locale.Name) || locale.Value.ValueKind != JsonValueKind.Object ||
                !TryGetString(locale.Value, "name", out var name) || string.IsNullOrWhiteSpace(name) ||
                !TryGetNullableString(locale.Value, DescriptionProperty, out var description, required: false))
            {
                return false;
            }
            projected[locale.Name] = new JsonObject { ["name"] = name, [DescriptionProperty] = description };
        }

        return true;
    }

    private static bool TryReadDependencies(JsonElement source, out JsonArray projected)
    {
        projected = new JsonArray();
        if (!source.TryGetProperty("dependencies", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var dependency in values.EnumerateArray())
        {
            if (!TryReadDependency(dependency, out var item))
            {
                return false;
            }
            projected.Add(item);
        }

        return true;
    }

    private static bool TryReadDependency(JsonElement dependency, out JsonObject projected)
    {
        projected = new JsonObject();
        if (dependency.ValueKind != JsonValueKind.Object ||
            !TryReadReference(dependency, out projected) ||
            !TryGetString(dependency, "role", out var role) || !DependencyRoles.Contains(role))
        {
            return false;
        }

        projected["role"] = role;
        return TryAddOptionalSortOrder(dependency, projected) &&
            TryAddOptionalIncludedByDefault(dependency, projected);
    }

    private static bool TryAddOptionalSortOrder(JsonElement dependency, JsonObject projected)
    {
        if (!dependency.TryGetProperty(SortOrderProperty, out var order))
        {
            return true;
        }

        if (order.ValueKind != JsonValueKind.Number || !order.TryGetInt32(out var sortOrder) || sortOrder < 0)
        {
            return false;
        }

        projected[SortOrderProperty] = sortOrder;
        return true;
    }

    private static bool TryAddOptionalIncludedByDefault(JsonElement dependency, JsonObject projected)
    {
        if (!dependency.TryGetProperty("includedByDefault", out var included))
        {
            return true;
        }

        if (included.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        projected["includedByDefault"] = included.GetBoolean();
        return true;
    }

    private static bool TryReadProvenance(JsonElement source, out JsonObject projected)
    {
        projected = new JsonObject();
        if (!source.TryGetProperty("provenance", out var value) || value.ValueKind != JsonValueKind.Object ||
            !TryGetString(value, "contentOrigin", out var origin) || !ProvenanceOrigins.Contains(origin) ||
            !TryGetString(value, "sourceDescription", out var sourceDescription) ||
            !TryGetString(value, LicenseProperty, out var license) ||
            !value.TryGetProperty("mediaAssets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var publicAssets = new JsonArray();
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !TryGetString(asset, "assetPath", out var assetPath) ||
                !TryGetString(asset, LicenseProperty, out var assetLicense) ||
                !TryGetString(asset, "evidenceRef", out var evidenceRef))
            {
                return false;
            }
            publicAssets.Add(new JsonObject
            {
                ["assetPath"] = assetPath,
                [LicenseProperty] = assetLicense,
                ["evidenceRef"] = evidenceRef
            });
        }

        projected = new JsonObject
        {
            ["contentOrigin"] = origin,
            ["sourceDescription"] = sourceDescription,
            [LicenseProperty] = license,
            ["mediaAssets"] = publicAssets
        };
        AddOptionalString(value, projected, "attribution");
        AddOptionalString(value, projected, "evidenceRef");
        return true;
    }

}
