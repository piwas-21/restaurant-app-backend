using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportReviewRules
{
    private const string OptionSetType = "option-set";

    public static string ExpectedEntityType(string templateType) => templateType switch
    {
        "category" => "Category",
        "ingredient" => "GlobalIngredient",
        "option-set" => "OptionSet",
        "bundle" => "MenuBundle",
        "item" => "Product",
        _ => string.Empty
    };

    public static void AddLocalReviewBlockers(
        string type,
        CatalogueImportItemDecision decision,
        List<CatalogueImportIssueDto> blockers)
    {
        if (type is not ("item" or "bundle"))
        {
            return;
        }

        if (decision.LocalPrice is null)
        {
            blockers.Add(Issue("TENANT_PRICE_REQUIRED", "Set a tenant-local price before import."));
        }
        else if (decision.LocalPrice <= 0)
        {
            blockers.Add(Issue("TENANT_PRICE_INVALID", "Tenant prices for sellable items and bundles must be greater than zero."));
        }

        if (type == "item" && !IsSupportedLocalProductType(decision.LocalProductType))
        {
            blockers.Add(Issue("TENANT_PRODUCT_TYPE_REQUIRED", "Choose the tenant product type for this item."));
        }

        if (decision.IntendedIsAvailable is null)
        {
            blockers.Add(Issue("TENANT_AVAILABILITY_CHOICE_REQUIRED", "Choose whether this item should be available after local publishing."));
        }

        if (type == "item" && decision.KitchenType is null)
        {
            blockers.Add(Issue("KITCHEN_TYPE_REQUIRED", "Choose the tenant kitchen routing for this item."));
        }

        if ((decision.LocalName ?? string.Empty).Trim().Length > 100)
        {
            blockers.Add(Issue("TENANT_NAME_TOO_LONG", "Product names must be 100 characters or fewer."));
        }

        if (decision.LocalDescription?.Length > 500)
        {
            blockers.Add(Issue("TENANT_DESCRIPTION_TOO_LONG", "Product descriptions must be 500 characters or fewer."));
        }

        AddReviewBlocker(decision.IngredientsReviewed, "INGREDIENT_REVIEW_REQUIRED", "Review this tenant's ingredients.", blockers);
        AddReviewBlocker(decision.AllergensReviewed, "ALLERGEN_REVIEW_REQUIRED", "Review this tenant's allergen claims.", blockers);
        AddReviewBlocker(decision.AvailabilityReviewed, "AVAILABILITY_REVIEW_REQUIRED", "Review tenant availability.", blockers);
        AddReviewBlocker(decision.ChannelsReviewed, "CHANNEL_REVIEW_REQUIRED", "Review eligible order channels.", blockers);
        AddReviewBlocker(decision.KitchenRoutingReviewed, "KITCHEN_ROUTING_REVIEW_REQUIRED", "Review kitchen routing.", blockers);
    }

    public static void AddChoiceReviewBlockers(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        List<CatalogueImportIssueDto> blockers)
    {
        var priceRefs = RequiredLocalPriceRefs(revision);
        var hasChoiceStructures = priceRefs.Length > 0 || HasChoiceStructures(revision) ||
            revision.Type == OptionSetType;
        if (!hasChoiceStructures)
        {
            return;
        }

        AddReviewBlocker(decision.ChoiceRulesReviewed, "CHOICE_RULES_REVIEW_REQUIRED", "Review the tenant choice rules and defaults.", blockers);
        AddUnsupportedIngredientCardinalityBlocker(revision, blockers);
        if (priceRefs.Length == 0)
        {
            return;
        }

        AddReviewBlocker(decision.OptionPricesReviewed, "OPTION_PRICES_REVIEW_REQUIRED", "Review local prices for every imported choice.", blockers);
        foreach (var sourceRef in priceRefs)
        {
            if (decision.LocalOptionPrices?.TryGetValue(sourceRef, out var price) != true)
            {
                blockers.Add(Issue("OPTION_PRICE_REQUIRED", $"Set a tenant-local price for {sourceRef}."));
            }
            else if (price < 0)
            {
                blockers.Add(Issue("OPTION_PRICE_INVALID", $"The tenant-local price for {sourceRef} cannot be negative."));
            }
        }
    }

    public static void AddUnsupportedPayloadBlockers(
        CentralCatalogueTemplateRevision revision,
        List<CatalogueImportIssueDto> blockers)
    {
        try
        {
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision);
        }
        catch (BadRequestException exception)
        {
            blockers.Add(Issue(exception.ErrorCode ?? "UNSUPPORTED_SOURCE_PAYLOAD", exception.Message));
        }
    }

    private static void AddUnsupportedIngredientCardinalityBlocker(
        CentralCatalogueTemplateRevision revision,
        List<CatalogueImportIssueDto> blockers)
    {
        if (revision.Type != OptionSetType)
        {
            return;
        }

        CatalogueOptionSetPayload optionSet;
        try
        {
            optionSet = CatalogueImportPayloadReader.ReadOptionSet(revision);
        }
        catch (BadRequestException)
        {
            // The general unsupported-payload blocker reports malformed option sets.
            return;
        }

        if (optionSet.Kind == "ingredient" &&
            (optionSet.Minimum > 0 || optionSet.Maximum < optionSet.Options.Count))
        {
            blockers.Add(Issue("UNSUPPORTED_INGREDIENT_CARDINALITY",
                "This ingredient set requires a selection minimum or maximum that the tenant's optional ingredient group cannot represent."));
        }
    }

    private static string[] RequiredLocalPriceRefs(CentralCatalogueTemplateRevision revision)
    {
        if (revision.Type == OptionSetType && revision.Payload.TryGetProperty("kind", out var kind) &&
            kind.ValueKind == JsonValueKind.String && kind.GetString() == "suggested-side")
        {
            return [];
        }

        if (revision.Type == "bundle" && revision.Payload.TryGetProperty("sections", out var sections) &&
            sections.ValueKind == JsonValueKind.Array)
        {
            return sections.EnumerateArray()
                .Where(section => section.ValueKind == JsonValueKind.Object &&
                    section.TryGetProperty("options", out var sectionOptions) && sectionOptions.ValueKind == JsonValueKind.Array)
                .SelectMany(section => ReadSourceRefs(section.GetProperty("options")))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (!revision.Payload.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return ReadSourceRefs(options).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ReadSourceRefs(JsonElement options) => options.EnumerateArray()
        .Where(option => option.ValueKind == JsonValueKind.Object &&
            option.TryGetProperty("templateId", out var id) && id.ValueKind == JsonValueKind.String &&
            option.TryGetProperty("revision", out var version) && version.ValueKind == JsonValueKind.Number &&
            version.TryGetInt32(out _))
        .Select(option => $"{option.GetProperty("templateId").GetString()}@{option.GetProperty("revision").GetInt32()}");

    private static bool HasChoiceStructures(CentralCatalogueTemplateRevision revision) =>
        revision.Type == "item" && (HasArray(revision.Payload, "optionSets") || HasArray(revision.Payload, "sideSets"));

    private static bool HasArray(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var array) &&
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0;

    private static bool IsSupportedLocalProductType(string? productType) => productType is
        "MainItem" or "Beverage" or "Dessert" or "Sauce" or "AddOn";

    private static void AddReviewBlocker(
        bool? reviewed,
        string code,
        string message,
        List<CatalogueImportIssueDto> blockers)
    {
        if (reviewed != true)
        {
            blockers.Add(Issue(code, message));
        }
    }

    private static CatalogueImportIssueDto Issue(string code, string message) => new(code, message);
}
