using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportTranslationMapper
{
    public static TranslationOwnerMetadataDto? OwnerMetadata(
        CentralCatalogueTemplateRevision revision,
        bool includeDescription = true)
    {
        if (!IsReviewed(revision)) return null;

        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = revision.SourceLocale
        };
        if (includeDescription && !string.IsNullOrWhiteSpace(revision.Description))
        {
            sources["description"] = revision.SourceLocale;
        }

        return new TranslationOwnerMetadataDto { SourceLocales = sources };
    }

    public static RecordTemplateTranslationRequest ForProduct(
        CentralCatalogueTemplateRevision revision,
        ProductDto product) => new(
        "product", product.Id, revision.TemplateId, revision.Revision, revision.SourceLocale,
        ProductText(product, revision.SourceLocale), ReviewedValues(revision));

    public static RecordTemplateTranslationRequest ForOptionSet(
        CentralCatalogueTemplateRevision revision,
        OptionSet optionSet) => new(
        "optionSet", optionSet.Id, revision.TemplateId, revision.Revision, revision.SourceLocale,
        TranslationTextMap.Create(optionSet.Name, null,
            optionSet.Translations.Select(value => (value.LanguageCode, (string?)value.Name, (string?)null))),
        ReviewedNames(revision.SourceLocale, revision.Name, revision.Translations.ToDictionary(
            pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase)));

    public static RecordTemplateTranslationRequest ForBundleSectionOptionSet(
        CentralCatalogueTemplateRevision revision,
        CatalogueBundleSection source,
        OptionSet optionSet) => new(
        "optionSet", optionSet.Id, revision.TemplateId, revision.Revision, revision.SourceLocale,
        TranslationTextMap.Create(optionSet.Name, null,
            optionSet.Translations.Select(value => (value.LanguageCode, (string?)value.Name, (string?)null))),
        ReviewedNames(revision.SourceLocale, source.Name, source.Translations));

    public static RecordTemplateTranslationRequest ForSection(
        CentralCatalogueTemplateRevision revision,
        CatalogueBundleSection source,
        MenuBundleSectionDto saved)
    {
        if (saved.Id == Guid.Empty)
        {
            throw new BadRequestException("Bundle creation did not persist a section identity.", "MENU_SECTION_MISSING");
        }

        return new RecordTemplateTranslationRequest(
            "menuSection", saved.Id, revision.TemplateId, revision.Revision, revision.SourceLocale,
            TranslationTextMap.Create(saved.Name, saved.Description,
                saved.Translations.Select(pair => (pair.Key, (string?)pair.Value.Name, pair.Value.Description))),
            ReviewedNames(revision.SourceLocale, source.Name, source.Translations));
    }

    private static TranslationTextMap ProductText(ProductDto product, string sourceLocale)
    {
        product.Content.TryGetValue(sourceLocale, out var source);
        return TranslationTextMap.Create(source?.Name ?? product.Name, source?.Description,
            product.Content.Select(pair => (pair.Key, (string?)pair.Value.Name, (string?)pair.Value.Description)));
    }

    private static List<TemplateTranslationEvidence> ReviewedValues(
        CentralCatalogueTemplateRevision revision)
    {
        var values = ReviewedNames(revision.SourceLocale, revision.Name, revision.Translations).ToList();
        if (!string.IsNullOrWhiteSpace(revision.Description))
        {
            values.Add(new TemplateTranslationEvidence("description", revision.SourceLocale, revision.Description));
        }

        values.AddRange(revision.Translations
            .Where(pair => !pair.Key.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value.Description))
            .Select(pair => new TemplateTranslationEvidence("description", pair.Key, pair.Value.Description!)));
        return values;
    }

    private static IReadOnlyList<TemplateTranslationEvidence> ReviewedNames(
        string sourceLocale,
        string sourceName,
        IReadOnlyDictionary<string, CentralCatalogueTranslation> translations) =>
        ReviewedNames(sourceLocale, sourceName, translations.ToDictionary(
            pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<TemplateTranslationEvidence> ReviewedNames(
        string sourceLocale,
        string sourceName,
        IReadOnlyDictionary<string, string> translations) =>
        [new TemplateTranslationEvidence("name", sourceLocale, sourceName),
         .. translations.Where(pair => !pair.Key.Equals(sourceLocale, StringComparison.OrdinalIgnoreCase))
             .Select(pair => new TemplateTranslationEvidence("name", pair.Key, pair.Value))];

    private static bool IsReviewed(CentralCatalogueTemplateRevision revision) =>
        string.Equals(revision.QualityStatus, "reviewed", StringComparison.OrdinalIgnoreCase);
}
