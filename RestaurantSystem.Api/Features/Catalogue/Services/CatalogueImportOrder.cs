using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportOrder
{
    public static IReadOnlyList<CatalogueImportSessionTemplate> SelectedDependenciesFirst(
        IReadOnlyCollection<CatalogueImportSessionTemplate> templates)
    {
        var byKey = templates.ToDictionary(item => (item.TemplateId, item.Revision));
        var ordered = new List<CatalogueImportSessionTemplate>(templates.Count);
        var visited = new HashSet<(string TemplateId, int Revision)>();
        var active = new HashSet<(string TemplateId, int Revision)>();

        foreach (var root in templates.Where(item => item.IsRoot))
        {
            Visit(root);
        }

        foreach (var selected in templates.Where(item => item.IsSelected))
        {
            Visit(selected);
        }

        return ordered;

        void Visit(CatalogueImportSessionTemplate item)
        {
            var key = (item.TemplateId, item.Revision);
            if (!item.IsSelected)
            {
                return;
            }

            if (active.Contains(key))
            {
                throw new BadRequestException("Catalogue dependency graph contains a cycle");
            }

            if (!visited.Add(key))
            {
                return;
            }

            active.Add(key);

            var revision = CatalogueSessionMapper.ParseRevision(item.RevisionJson);
            foreach (var dependency in revision.Dependencies
                         .OrderBy(value => value.SortOrder ?? int.MaxValue)
                         .ThenBy(value => value.TemplateId, StringComparer.Ordinal))
            {
                if (byKey.TryGetValue((dependency.TemplateId, dependency.Revision), out var child))
                {
                    Visit(child);
                }
            }

            active.Remove(key);
            ordered.Add(item);
        }
    }

    public static IReadOnlyList<(string TemplateId, int Revision)> SelectedPrerequisites(
        CatalogueImportSessionTemplate item)
    {
        var revision = CatalogueSessionMapper.ParseRevision(item.RevisionJson);
        return revision.Dependencies
            .Select(dependency => (dependency.TemplateId, dependency.Revision))
            .ToArray();
    }
}
