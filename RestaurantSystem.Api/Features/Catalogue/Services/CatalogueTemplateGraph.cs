using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed record CatalogueTemplateGraph(IReadOnlyList<CatalogueTemplateGraphNode> Nodes)
{
    public CatalogueTemplateGraphNode Root => Nodes.Single(node => node.IsRoot);
}

public sealed record CatalogueTemplateGraphNode(
    CentralCatalogueTemplateRevision Revision,
    string SelectionRole,
    bool IsRoot,
    bool IsSelectable);
