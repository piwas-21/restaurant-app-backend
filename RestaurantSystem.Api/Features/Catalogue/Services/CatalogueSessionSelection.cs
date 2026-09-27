using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public static class CatalogueSessionSelection
{
    public static void Recompute(
        CatalogueImportSession session,
        IReadOnlyCollection<string>? explicitOptionalSelection)
    {
        var selected = ComputeSelectedKeys(session, explicitOptionalSelection);
        foreach (var item in session.Templates)
        {
            item.IsSelected = selected.Contains((item.TemplateId, item.Revision));
        }
    }

    public static HashSet<(string TemplateId, int Revision)> ComputeSelectedKeys(
        CatalogueImportSession session,
        IReadOnlyCollection<string>? explicitOptionalSelection)
    {
        var byKey = session.Templates.ToDictionary(item => (item.TemplateId, item.Revision));
        var optionalIds = session.Templates.Where(item => item.IsSelectable)
            .Select(item => item.TemplateId).ToHashSet(StringComparer.Ordinal);
        var selectedIds = explicitOptionalSelection?.ToHashSet(StringComparer.Ordinal);
        ValidateOptionalSelection(selectedIds, optionalIds);

        var root = session.Templates.SingleOrDefault(item => item.IsRoot)
            ?? throw new BadRequestException("Import session has no root template");
        var selected = new HashSet<(string TemplateId, int Revision)> { (root.TemplateId, root.Revision) };
        Visit(root, byKey, selected, selectedIds);
        return selected;
    }

    private static void ValidateOptionalSelection(
        HashSet<string>? selectedIds,
        HashSet<string> optionalIds)
    {
        if (selectedIds is not null && selectedIds.Except(optionalIds).Any())
        {
            throw new BadRequestException("Selection contains a template that is not an optional dependency");
        }
    }

    private static void Visit(
        CatalogueImportSessionTemplate parent,
        IReadOnlyDictionary<(string TemplateId, int Revision), CatalogueImportSessionTemplate> byKey,
        HashSet<(string TemplateId, int Revision)> selected,
        HashSet<string>? selectedIds)
    {
        using var document = JsonDocument.Parse(parent.RevisionJson);
        var revision = CatalogueTemplateGraphLoader.Deserialize(document.RootElement);
        foreach (var dependency in revision.Dependencies)
        {
            if (!byKey.TryGetValue((dependency.TemplateId, dependency.Revision), out var child))
            {
                throw new BadRequestException("Import session dependency graph is incomplete");
            }

            if (ShouldSelectDependency(revision, dependency, parent, selected, selectedIds) &&
                selected.Add((child.TemplateId, child.Revision)))
            {
                Visit(child, byKey, selected, selectedIds);
            }
        }
    }

    private static bool ShouldSelectDependency(
        CentralCatalogueTemplateRevision revision,
        CentralCatalogueDependency dependency,
        CatalogueImportSessionTemplate parent,
        HashSet<(string TemplateId, int Revision)> selected,
        HashSet<string>? selectedIds)
    {
        if (!selected.Contains((parent.TemplateId, parent.Revision))) return false;
        if (dependency.IncludedByDefault is null) return true;
        if (selectedIds is not null) return selectedIds.Contains(dependency.TemplateId);
        return revision.Type == "cuisine-pack" && dependency.Role == "offer" || dependency.IncludedByDefault.Value;
    }
}
