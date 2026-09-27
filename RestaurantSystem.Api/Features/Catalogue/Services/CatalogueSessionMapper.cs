using System.Text.Json;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueSessionMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public static CatalogueImportSessionDto ToDto(CatalogueImportSession session)
    {
        var items = session.Templates
            .OrderBy(item => item.IsRoot ? 0 : 1)
            .ThenBy(item => item.Type, StringComparer.Ordinal)
            .ThenBy(item => item.TemplateId, StringComparer.Ordinal)
            .Select(item =>
            {
                var revision = ParseRevision(item.RevisionJson);
                return new CatalogueImportSessionTemplateDto(
                    item.TemplateId,
                    item.Revision,
                    item.Type,
                    Localized(revision, session.Locale).Name,
                    Localized(revision, session.Locale).Description,
                    item.ContentHash,
                    item.IsRoot,
                    item.IsSelectable,
                    item.IsSelected,
                    item.SelectionRole,
                    item.Status.ToString(),
                    item.LocalEntityType,
                    item.LocalEntityId,
                    item.FailureCode,
                    item.DecisionJson is null
                        ? null
                        : JsonSerializer.Deserialize<CatalogueImportItemDecision>(item.DecisionJson, JsonOptions));
            })
            .ToArray();

        return new CatalogueImportSessionDto(
            session.Id,
            session.RootTemplateId,
            session.RootRevision,
            session.Locale,
            session.Version,
            session.Status.ToString(),
            session.CreateNewCopy,
            items);
    }

    public static CentralCatalogueTemplateRevision ParseRevision(string revisionJson)
    {
        using var document = JsonDocument.Parse(revisionJson);
        return CatalogueTemplateGraphLoader.Deserialize(document.RootElement);
    }

    public static (string Name, string? Description) Localized(
        CentralCatalogueTemplateRevision revision,
        string locale)
    {
        foreach (var candidate in LocaleCandidates(revision, locale))
        {
            if (string.Equals(candidate, revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            {
                return Canonical(revision);
            }

            var pair = revision.Translations.FirstOrDefault(value =>
                string.Equals(value.Key, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Value?.Name))
            {
                return (pair.Value.Name.Trim(), string.IsNullOrWhiteSpace(pair.Value.Description)
                    ? null
                    : pair.Value.Description.Trim());
            }
        }

        return Canonical(revision);
    }

    private static (string Name, string? Description) Canonical(CentralCatalogueTemplateRevision revision) =>
        (revision.Name.Trim(), string.IsNullOrWhiteSpace(revision.Description)
            ? null
            : revision.Description.Trim());

    public static string SerializeDecision(CatalogueImportItemDecision decision) =>
        JsonSerializer.Serialize(decision, JsonOptions);

    private static IEnumerable<string> LocaleCandidates(CentralCatalogueTemplateRevision revision, string locale)
    {
        yield return locale;
        var baseLanguage = locale.Split('-', 2)[0];
        if (!string.Equals(baseLanguage, locale, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseLanguage;
        }

        foreach (var fallback in revision.LocaleFallbacks)
        {
            yield return fallback;
        }

        yield return revision.SourceLocale;
    }
}
