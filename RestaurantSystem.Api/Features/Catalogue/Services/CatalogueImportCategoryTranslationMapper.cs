using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportCategoryTranslationMapper
{
    public static TranslationOwnerMetadataDto? OwnerMetadata(
        CentralCatalogueTemplateRevision revision,
        string sourceLocale)
    {
        if (!IsReviewed(revision)) return null;
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = sourceLocale };
        if (!string.IsNullOrWhiteSpace(revision.Description)) sources["description"] = sourceLocale;
        return new TranslationOwnerMetadataDto { SourceLocales = sources };
    }

    public static RecordTemplateTranslationRequest ForCategory(
        CentralCatalogueTemplateRevision revision,
        string sourceLocale,
        CategoryDto category) => new(
        "category", category.Id, revision.TemplateId, revision.Revision, sourceLocale,
        TranslationTextMap.Create(category.Name, category.Description,
            category.Translations.Select(pair =>
                (pair.Key, (string?)pair.Value.Name, pair.Value.Description))),
        ReviewedValues(revision));

    public static bool IsReviewed(CentralCatalogueTemplateRevision revision) =>
        string.Equals(revision.QualityStatus, "reviewed", StringComparison.OrdinalIgnoreCase);

    private static List<TemplateTranslationEvidence> ReviewedValues(
        CentralCatalogueTemplateRevision revision)
    {
        var values = new List<TemplateTranslationEvidence>
        {
            new("name", revision.SourceLocale, revision.Name.Trim())
        };
        if (!string.IsNullOrWhiteSpace(revision.Description))
        {
            values.Add(new TemplateTranslationEvidence("description", revision.SourceLocale, revision.Description));
        }

        foreach (var (locale, translation) in revision.Translations)
        {
            if (locale.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(translation.Name))
            {
                values.Add(new TemplateTranslationEvidence("name", locale, translation.Name));
            }

            if (!string.IsNullOrWhiteSpace(translation.Description))
            {
                values.Add(new TemplateTranslationEvidence("description", locale, translation.Description));
            }
        }

        return values;
    }
}
