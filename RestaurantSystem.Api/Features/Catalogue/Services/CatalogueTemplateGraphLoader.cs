using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueTemplateGraphLoader(ICentralCatalogueClient client) : ICatalogueTemplateGraphLoader
{
    private const int MaximumGraphNodes = 128;
    private const int MaximumGraphDepth = 8;
    private const int TenantContractVersion = 1;
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
    private static readonly Regex TemplateIdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, RegexMatchTimeout);
    private static readonly HashSet<string> SupportedTypes =
        ["ingredient", "option-set", "item", "bundle", "category", "cuisine-pack"];

    public async Task<CatalogueTemplateGraph> LoadAsync(
        string rootTemplateId,
        int rootRevision,
        CancellationToken cancellationToken)
    {
        var context = new GraphTraversalContext(
            new Dictionary<(string TemplateId, int Revision), CatalogueTemplateGraphNode>(),
            new HashSet<(string TemplateId, int Revision)>(),
            cancellationToken);
        await VisitAsync(context, new GraphVisit(rootTemplateId, rootRevision, "root", true, false, 0, true));
        return new CatalogueTemplateGraph(context.Nodes.Values.ToArray());
    }

    private async Task VisitAsync(
        GraphTraversalContext context,
        GraphVisit visit)
    {
        var key = (visit.TemplateId, visit.Revision);
        EnsureDepthWithinLimit(visit.Depth);
        if (!context.ActivePath.Add(key))
        {
            throw new BadRequestException("Catalogue dependency graph contains a cycle");
        }

        if (PromoteExistingNode(context.Nodes, context.ActivePath, key, visit.IsSelectable))
        {
            return;
        }

        EnsureNodeCanBeAdded(context.Nodes, visit.TemplateId, visit.Revision);
        var revisionDocument = await GetRevisionDocumentAsync(
            visit.TemplateId, visit.Revision, visit.IsRootRevision, context.CancellationToken);
        context.Nodes.Add(key, new CatalogueTemplateGraphNode(
            revisionDocument, visit.Role, visit.IsRoot, visit.IsSelectable));
        await VisitDependenciesAsync(context, visit, revisionDocument);
        context.ActivePath.Remove(key);
    }

    private async Task VisitDependenciesAsync(
        GraphTraversalContext context,
        GraphVisit parent,
        CentralCatalogueTemplateRevision revisionDocument)
    {
        foreach (var dependency in revisionDocument.Dependencies)
        {
            if (IsInvalidDependency(dependency))
            {
                throw new BadRequestException("A catalogue revision contains an invalid dependency");
            }

            await VisitAsync(context, new GraphVisit(
                dependency.TemplateId,
                dependency.Revision,
                dependency.Role,
                false,
                dependency.IncludedByDefault.HasValue,
                parent.Depth + 1,
                false));
        }
    }

    private async Task<CentralCatalogueTemplateRevision> GetRevisionDocumentAsync(
        string templateId,
        int revision,
        bool isRoot,
        CancellationToken cancellationToken)
    {
        var response = await client.GetRevisionAsync(templateId, revision, cancellationToken);
        if (response.StatusCode == StatusCodes.Status503ServiceUnavailable)
        {
            throw new ServiceUnavailableException("Catalogue is unavailable. Try again later.");
        }

        if (response.StatusCode == StatusCodes.Status404NotFound)
        {
            throw isRoot
                ? new NotFoundException("The reviewed catalogue revision is not available")
                : new BadRequestException("A reviewed catalogue dependency is not available");
        }

        if (response.StatusCode != StatusCodes.Status200OK)
        {
            throw new ServiceUnavailableException("Catalogue is unavailable. Try again later.");
        }

        var revisionDocument = Deserialize(response.Body);
        ValidateRevisionDocument(revisionDocument, templateId, revision);
        return revisionDocument;
    }

    private static void EnsureNodeCanBeAdded(
        Dictionary<(string TemplateId, int Revision), CatalogueTemplateGraphNode> nodes,
        string templateId,
        int revision)
    {
        if (nodes.Count >= MaximumGraphNodes)
        {
            throw new BadRequestException("Catalogue dependency graph exceeds the supported size");
        }

        if (nodes.Keys.Any(existingKey => existingKey.TemplateId == templateId && existingKey.Revision != revision))
        {
            throw new BadRequestException("Catalogue dependency graph contains multiple revisions of one template");
        }
    }

    private static bool PromoteExistingNode(
        Dictionary<(string TemplateId, int Revision), CatalogueTemplateGraphNode> nodes,
        HashSet<(string TemplateId, int Revision)> activePath,
        (string TemplateId, int Revision) key,
        bool isSelectable)
    {
        if (!nodes.TryGetValue(key, out var existing))
        {
            return false;
        }

        if (isSelectable && !existing.IsSelectable)
        {
            nodes[key] = existing with { IsSelectable = true };
        }

        activePath.Remove(key);
        return true;
    }

    private static void EnsureDepthWithinLimit(int depth)
    {
        if (depth > MaximumGraphDepth)
        {
            throw new BadRequestException("Catalogue dependency graph exceeds the supported depth");
        }
    }

    private static bool IsInvalidDependency(CentralCatalogueDependency dependency) =>
        string.IsNullOrWhiteSpace(dependency.TemplateId) || dependency.Revision < 1 ||
        string.IsNullOrWhiteSpace(dependency.Role) || dependency.Role.Length > 32 ||
        !TemplateIdPattern.IsMatch(dependency.TemplateId);

    private sealed record GraphTraversalContext(
        Dictionary<(string TemplateId, int Revision), CatalogueTemplateGraphNode> Nodes,
        HashSet<(string TemplateId, int Revision)> ActivePath,
        CancellationToken CancellationToken);

    private sealed record GraphVisit(
        string TemplateId,
        int Revision,
        string Role,
        bool IsRoot,
        bool IsSelectable,
        int Depth,
        bool IsRootRevision);

    public static CentralCatalogueTemplateRevision Deserialize(JsonElement body)
    {
        try
        {
            return JsonSerializer.Deserialize<CentralCatalogueTemplateRevision>(body.GetRawText(), JsonOptions)
                ?? throw new BadRequestException("Catalogue returned an invalid template revision");
        }
        catch (JsonException)
        {
            throw new BadRequestException("Catalogue returned an invalid template revision");
        }
    }

    internal static void ValidateRevisionDocument(
        CentralCatalogueTemplateRevision revision,
        string requestedTemplateId,
        int? requestedRevision)
    {
        if (revision.SchemaVersion != 1 || revision.TemplateId != requestedTemplateId ||
            revision.Revision < 1 || requestedRevision is not null && revision.Revision != requestedRevision ||
            !SupportedTypes.Contains(revision.Type) ||
            revision.QualityStatus != "reviewed" || string.IsNullOrWhiteSpace(revision.ContentHash) ||
            revision.ContentHash.Length > 128 || !revision.CompatibleTenantContractVersions.Contains(TenantContractVersion) ||
            string.IsNullOrWhiteSpace(revision.Name) || revision.Name.Length > 200)
        {
            throw new BadRequestException("Catalogue revision is not compatible with this tenant");
        }
    }
}
