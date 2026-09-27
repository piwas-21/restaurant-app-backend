using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CataloguePublishedRevisionLoader(ICentralCatalogueClient catalogue)
{
    public async Task<IReadOnlyDictionary<string, CataloguePublishedRevisionResult>> LoadBatchAsync(
        IReadOnlyList<CatalogueCurrentRevisionRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return new Dictionary<string, CataloguePublishedRevisionResult>(StringComparer.Ordinal);
        }

        if (requests.Any(request => string.IsNullOrWhiteSpace(request.TemplateId)) ||
            requests.Select(request => request.TemplateId).Distinct(StringComparer.Ordinal).Count() != requests.Count)
        {
            return UnavailableBatch(requests);
        }

        var results = new Dictionary<string, CataloguePublishedRevisionResult>(StringComparer.Ordinal);
        foreach (var requestBatch in requests.Chunk(CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems))
        {
            var batch = requestBatch.ToArray();
            var response = await catalogue.GetCurrentRevisionBatchAsync(batch, cancellationToken);
            if (response.StatusCode != StatusCodes.Status200OK || !TryReadBatchItems(response.Body, batch, out var items))
            {
                foreach (var request in batch)
                {
                    results.Add(request.TemplateId, Unavailable(
                        "Catalogue status is temporarily unavailable; the tenant record was not changed."));
                }

                continue;
            }

            foreach (var item in items)
            {
                var status = item.GetProperty("status").GetString();
                var templateId = item.GetProperty("templateId").GetString()!;
                var adoptedRevisionWithdrawn = ReadNullableBoolean(item.GetProperty("adoptedRevisionWithdrawn"));
                if (status == "notFound")
                {
                    results.Add(templateId, new CataloguePublishedRevisionResult("Unknown", null, null,
                        "Catalogue no longer recognizes this template ID; the tenant record was not changed."));
                    continue;
                }

                if (status == "withdrawn")
                {
                    var metadata = new CatalogueCurrentRevisionMetadata(templateId, null, null, true,
                        adoptedRevisionWithdrawn);
                    var resultStatus = adoptedRevisionWithdrawn == true ? "AdoptedRevisionWithdrawn" : "Withdrawn";
                    results.Add(templateId, new CataloguePublishedRevisionResult(resultStatus, metadata, null,
                        "This template is withdrawn from new adoption; the tenant record remains unchanged."));
                    continue;
                }

                try
                {
                    var revision = CatalogueTemplateGraphLoader.Deserialize(item.GetProperty("revision"));
                    CatalogueTemplateGraphLoader.ValidateRevisionDocument(revision, templateId, null);
                    var metadata = new CatalogueCurrentRevisionMetadata(templateId, revision.Revision,
                        revision.ContentHash, false, adoptedRevisionWithdrawn);
                    results.Add(templateId, new CataloguePublishedRevisionResult("Available", metadata, revision, null));
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or BadRequestException)
                {
                    results.Add(templateId, Unavailable(
                        "Catalogue returned an invalid revision; the tenant record was not changed."));
                }
            }
        }

        return results;
    }

    private static CataloguePublishedRevisionResult Unavailable(string notice) =>
        new("Unavailable", null, null, notice);

    private static Dictionary<string, CataloguePublishedRevisionResult> UnavailableBatch(
        IEnumerable<CatalogueCurrentRevisionRequest> requests) => requests
        .Where(request => !string.IsNullOrWhiteSpace(request.TemplateId))
        .GroupBy(request => request.TemplateId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key,
            _ => Unavailable("Catalogue status is temporarily unavailable; the tenant record was not changed."),
            StringComparer.Ordinal);

    private static bool TryReadBatchItems(
        System.Text.Json.JsonElement body,
        CatalogueCurrentRevisionRequest[] requests,
        out List<System.Text.Json.JsonElement> items)
    {
        items = [];
        if (body.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !body.TryGetProperty("items", out var itemArray) ||
            itemArray.ValueKind != System.Text.Json.JsonValueKind.Array ||
            itemArray.GetArrayLength() != requests.Length)
        {
            return false;
        }

        var expectedIds = requests.Select(request => request.TemplateId).ToHashSet(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in itemArray.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !item.TryGetProperty("templateId", out var idValue) ||
                idValue.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return false;
            }

            var id = idValue.GetString();
            if (string.IsNullOrWhiteSpace(id) || !expectedIds.Contains(id) || !seenIds.Add(id) ||
                !item.TryGetProperty("status", out var status) || status.ValueKind != System.Text.Json.JsonValueKind.String ||
                !item.TryGetProperty("revision", out var revision) ||
                !item.TryGetProperty("adoptedRevisionWithdrawn", out var adoptedWithdrawn) ||
                adoptedWithdrawn.ValueKind is not (
                    System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False or
                    System.Text.Json.JsonValueKind.Null))
            {
                return false;
            }

            var statusText = status.GetString();
            if (statusText == "available")
            {
                if (revision.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return false;
                }
            }
            else if (statusText is "withdrawn" or "notFound")
            {
                if (revision.ValueKind != System.Text.Json.JsonValueKind.Null ||
                    statusText == "notFound" &&
                    adoptedWithdrawn.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            items.Add(item.Clone());
        }

        return seenIds.Count == expectedIds.Count;
    }

    private static bool? ReadNullableBoolean(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => null
    };
}

internal sealed record CataloguePublishedRevisionResult(
    string Status,
    CatalogueCurrentRevisionMetadata? Metadata,
    CentralCatalogueTemplateRevision? Revision,
    string? Notice);
