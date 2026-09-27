using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CataloguePublishedRevisionLoader(ICentralCatalogueClient catalogue)
{
    private const string AvailableStatus = "available";
    private const string WithdrawnStatus = "withdrawn";
    private const string NotFoundStatus = "notFound";

    public async Task<IReadOnlyDictionary<string, CataloguePublishedRevisionResult>> LoadBatchAsync(
        IReadOnlyList<CatalogueCurrentRevisionRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return new Dictionary<string, CataloguePublishedRevisionResult>(StringComparer.Ordinal);
        }

        if (HasInvalidRequests(requests))
        {
            return UnavailableBatch(requests);
        }

        return await LoadChunksAsync(requests, cancellationToken);
    }

    private async Task<Dictionary<string, CataloguePublishedRevisionResult>> LoadChunksAsync(
        IReadOnlyList<CatalogueCurrentRevisionRequest> requests,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, CataloguePublishedRevisionResult>(StringComparer.Ordinal);
        foreach (var requestBatch in requests.Chunk(CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems))
        {
            await AddBatchResultsAsync(results, requestBatch, cancellationToken);
        }

        return results;
    }

    private async Task AddBatchResultsAsync(
        Dictionary<string, CataloguePublishedRevisionResult> results,
        IEnumerable<CatalogueCurrentRevisionRequest> requestBatch,
        CancellationToken cancellationToken)
    {
        var batch = requestBatch.ToArray();
        var response = await catalogue.GetCurrentRevisionBatchAsync(batch, cancellationToken);
        if (response.StatusCode != StatusCodes.Status200OK ||
            !TryReadBatchItems(response.Body, batch, out var items))
        {
            AddUnavailableResults(results, batch);
            return;
        }

        AddReadResults(results, items);
    }

    private static void AddReadResults(
        Dictionary<string, CataloguePublishedRevisionResult> results,
        IEnumerable<System.Text.Json.JsonElement> items)
    {
        foreach (var item in items)
        {
            var templateId = item.GetProperty("templateId").GetString()!;
            results.Add(templateId, ReadResult(item, templateId));
        }
    }

    private static bool HasInvalidRequests(IReadOnlyList<CatalogueCurrentRevisionRequest> requests) =>
        requests.Any(request => string.IsNullOrWhiteSpace(request.TemplateId)) ||
        requests.Select(request => request.TemplateId).Distinct(StringComparer.Ordinal).Count() != requests.Count;

    private static void AddUnavailableResults(
        Dictionary<string, CataloguePublishedRevisionResult> results,
        IEnumerable<CatalogueCurrentRevisionRequest> requests)
    {
        foreach (var request in requests)
        {
            results.Add(request.TemplateId, Unavailable(
                "Catalogue status is temporarily unavailable; the tenant record was not changed."));
        }
    }

    private static CataloguePublishedRevisionResult ReadResult(System.Text.Json.JsonElement item, string templateId)
    {
        var status = item.GetProperty("status").GetString();
        var adoptedRevisionWithdrawn = ReadNullableBoolean(item.GetProperty("adoptedRevisionWithdrawn"));
        if (status == NotFoundStatus)
        {
            return UnknownResult();
        }

        if (status == WithdrawnStatus)
        {
            return WithdrawnResult(templateId, adoptedRevisionWithdrawn);
        }

        return AvailableResult(item, templateId, adoptedRevisionWithdrawn);
    }

    private static CataloguePublishedRevisionResult UnknownResult() =>
        new("Unknown", null, null,
            "Catalogue no longer recognizes this template ID; the tenant record was not changed.");

    private static CataloguePublishedRevisionResult WithdrawnResult(
        string templateId,
        bool? adoptedRevisionWithdrawn)
    {
        var metadata = new CatalogueCurrentRevisionMetadata(templateId, null, null, true,
            adoptedRevisionWithdrawn);
        var resultStatus = adoptedRevisionWithdrawn == true ? "AdoptedRevisionWithdrawn" : "Withdrawn";
        return new CataloguePublishedRevisionResult(resultStatus, metadata, null,
            "This template is withdrawn from new adoption; the tenant record remains unchanged.");
    }

    private static CataloguePublishedRevisionResult AvailableResult(
        System.Text.Json.JsonElement item,
        string templateId,
        bool? adoptedRevisionWithdrawn)
    {
        try
        {
            var revision = CatalogueTemplateGraphLoader.Deserialize(item.GetProperty("revision"));
            CatalogueTemplateGraphLoader.ValidateRevisionDocument(revision, templateId, null);
            var metadata = new CatalogueCurrentRevisionMetadata(templateId, revision.Revision,
                revision.ContentHash, false, adoptedRevisionWithdrawn);
            return new CataloguePublishedRevisionResult("Available", metadata, revision, null);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or BadRequestException)
        {
            return Unavailable("Catalogue returned an invalid revision; the tenant record was not changed.");
        }
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
            if (!TryReadValidItem(item, expectedIds, seenIds))
            {
                return false;
            }

            items.Add(item.Clone());
        }

        return seenIds.Count == expectedIds.Count;
    }

    private static bool TryReadValidItem(
        System.Text.Json.JsonElement item,
        HashSet<string> expectedIds,
        HashSet<string> seenIds)
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

        return HasValidStatusPayload(status.GetString(), revision, adoptedWithdrawn);
    }

    private static bool HasValidStatusPayload(
        string? status,
        System.Text.Json.JsonElement revision,
        System.Text.Json.JsonElement adoptedWithdrawn)
    {
        if (status == AvailableStatus)
        {
            return revision.ValueKind == System.Text.Json.JsonValueKind.Object;
        }

        if (status is WithdrawnStatus or NotFoundStatus)
        {
            return revision.ValueKind == System.Text.Json.JsonValueKind.Null &&
                (status != NotFoundStatus || adoptedWithdrawn.ValueKind == System.Text.Json.JsonValueKind.Null);
        }

        return false;
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
