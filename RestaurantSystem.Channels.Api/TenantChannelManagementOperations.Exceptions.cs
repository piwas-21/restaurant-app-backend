using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelExceptionService(TenantManagementContext context,
    ITenantChannelExceptionData data, ITenantChannelPublicationReconciler publicationReconciler,
    ITenantChannelAvailabilityReconciler availabilityReconciler) : ITenantChannelExceptionService
{
    private const int PageSize = 50;
    private const string Open = "open";
    private const string Reconciling = "reconciling";
    private const string Warning = "warning";
    private const string Uncertain = "uncertain";
    private const string AvailabilityPrefix = "availability:";
    private const string InvalidCursor = "Refresh the exception inbox for a valid page.";

    public async Task<JsonElement> Exceptions(string cursor, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var page = DecodeCursor(cursor);
        var imports = await data.Imports(page.ImportCursor ?? string.Empty, cancellationToken);
        var rows = await ReadPageRows(page.IncludeCurrent, imports, cancellationToken);
        var ordered = rows.DistinctBy(row => row.Id).OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Id).ToArray();
        var items = ordered.Skip(page.Offset).Take(PageSize).ToArray();
        var next = NextCursor(page, items, ordered.Length, imports);
        return ProviderJson.Encode(new { items = items.Select(ExceptionWire), nextCursor = next, checkedAt = context.Clock.GetUtcNow() });
    }

    public async Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken)
    {
        var cursor = string.Empty;
        do
        {
            var rows = await Exceptions(cursor, cancellationToken);
            if (FindException(rows, exceptionId) is { } found) return found;
            cursor = ReadNextCursor(rows);
        } while (cursor.Length > 0);
        throw new ChannelConsoleException(404, "Exception was not found.");
    }

    public async Task<JsonElement> Reconcile(Guid exceptionId, Guid actorId, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var publication = await data.FindPublication(exceptionId, cancellationToken);
        if (publication is not null) return await publicationReconciler.Reconcile(publication, actorId, cancellationToken);
        if (await IsDisconnected(exceptionId, cancellationToken))
            return ChannelManagementJson.Result(exceptionId, Open, "IntegrationDisconnected", false);
        var availability = await FindAvailabilityState(exceptionId, cancellationToken);
        if (availability is not null)
            return await availabilityReconciler.Reconcile(availability, actorId, cancellationToken);
        return ChannelManagementJson.Result(exceptionId, Open, "ReviewRequired", false);
    }

    private async Task<IReadOnlyList<ExceptionView>> ReadPageRows(bool includeCurrent, JsonElement imports,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExceptionView>();
        rows.AddRange(ImportExceptions(imports, context.Clock.GetUtcNow()));
        if (!includeCurrent) return rows;
        rows.AddRange(await PublicationExceptions(cancellationToken));
        rows.AddRange(await AvailabilityExceptions(cancellationToken));
        rows.AddRange(await AuditExceptions(cancellationToken));
        return rows;
    }

    private static string? NextCursor(ExceptionCursor page, IReadOnlyCollection<ExceptionView> items,
        int total, JsonElement imports)
    {
        if (page.Offset + items.Count < total)
            return EncodeCursor(page with { Offset = page.Offset + items.Count });
        if (ProviderJson.Flag(imports, "truncated") && ProviderJson.Text(imports, "nextCursor") is { Length: > 0 } importCursor)
            return EncodeCursor(new(false, importCursor, 0));
        return null;
    }

    private static JsonElement? FindException(JsonElement response, Guid id)
    {
        if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in items.EnumerateArray())
            if (Guid.TryParse(ProviderJson.Text(item, "id"), out var parsed) && parsed == id) return item.Clone();
        return null;
    }

    private static string ReadNextCursor(JsonElement response)
        => response.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String
            ? next.GetString() ?? string.Empty : string.Empty;

    private async Task<IReadOnlyList<ExceptionView>> PublicationExceptions(CancellationToken cancellationToken)
    {
        var latest = await data.LatestPublication(cancellationToken);
        if (latest is not { State: CataloguePublicationStates.Pending }) return [];
        return [new(latest.Id, "publication", Warning, Reconciling, "ProviderReadbackPending",
            "Menu publication needs readback", "Uber has not confirmed the published menu yet.", latest.VerifiedAt ?? context.Clock.GetUtcNow(),
            latest.Id, null, null, true, true)];
    }

    private async Task<IReadOnlyList<ExceptionView>> AvailabilityExceptions(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelAvailabilityState> rows;
        try { rows = await data.AvailabilityStates(cancellationToken); }
        catch (ChannelConsoleException) { return []; }
        return rows.Where(row => row.State != CataloguePublicationStates.Verified)
            .Select(row => AvailabilityException(row, context.Clock.GetUtcNow())).ToArray();
    }

    private static ExceptionView AvailabilityException(ChannelAvailabilityState row, DateTimeOffset now)
        => new(ChannelManagementJson.StableId(AvailabilityPrefix + row.ProviderItemId), "availability", Warning,
            row.State == "Uncertain" ? Reconciling : Open, "Availability" + row.State,
            "Uber item availability is not confirmed", "Refresh the provider readback before taking action.",
            row.VerifiedAt ?? now, null, null, null, true, true);

    private static ExceptionView[] ImportExceptions(JsonElement imports, DateTimeOffset now)
    {
        if (!imports.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        return rows.EnumerateArray().Select(row => ImportException(row, now)).Where(row => row is not null).Select(row => row!).ToArray();
    }

    private static ExceptionView? ImportException(JsonElement row, DateTimeOffset now)
    {
        var state = ProviderJson.Text(row, "state"); var code = ProviderJson.Text(row, "code");
        if (state == "Imported" || code.Length == 0 && state != "Quarantined") return null;
        var rawId = ProviderJson.Text(row, "orderId");
        var providerId = Guid.TryParse(rawId, out var parsed) ? parsed : (Guid?)null;
        var localId = Guid.TryParse(ProviderJson.Text(row, "tenantOrderId"), out var local) ? local : (Guid?)null;
        var id = providerId ?? ChannelManagementJson.StableId("import:" + rawId);
        var updated = ParseTime(row, "updatedAt") ?? now;
        var created = ParseTime(row, "createdAt") ?? updated;
        var quarantined = state == "Quarantined";
        return new(id, "import", quarantined ? "critical" : Warning, quarantined ? Open : Reconciling,
            SafeCode(code), "Uber order needs review", "The order is held for operator review. No payment was collected by Sofra.",
            created, id, providerId, localId, false, true)
        { UpdatedAt = updated };
    }

    private async Task<IReadOnlyList<ExceptionView>> AuditExceptions(CancellationToken cancellationToken)
    {
        var records = await data.Audit(cancellationToken);
        var rows = new List<ExceptionView>();
        var latestOAuth = records.FirstOrDefault(row => row.Action is "OAuthStart" or "OAuth");
        if (latestOAuth is { Action: "OAuth", OperationId: { } flowId })
            await AddOAuthException(rows, flowId, cancellationToken);
        var connection = await data.Connection(cancellationToken);
        if (connection.IsDisconnected) rows.Add(DisconnectedException(connection, context.Clock.GetUtcNow()));
        return rows;
    }

    private async Task AddOAuthException(List<ExceptionView> rows, Guid flowId, CancellationToken cancellationToken)
    {
        var flow = await data.OAuthFlow(flowId, cancellationToken);
        if (flow is not { Status: "Failed" or "Expired" }) return;
        rows.Add(new(flow.Id, "connection", Warning, Open, SafeCode(flow.ErrorCode ?? "ConnectionUnconfirmed"),
            "Uber authorization needs attention", "Reconnect to the approved store and review the confirmed connection status.",
            flow.CreatedAt, flow.Id, null, null, false, true)
        { UpdatedAt = flow.CompletedAt ?? flow.CreatedAt });
    }

    private static ExceptionView DisconnectedException(ChannelManagementConnectionState connection, DateTimeOffset now)
        => new(ChannelManagementJson.StableId("connection-disconnected"), "connection", Warning, Open,
            "IntegrationDisconnected", "Uber connection is disconnected",
            "Reconnect and confirm the approved store before accepting new orders.",
            connection.UpdatedAt ?? now, null, null, null, false, true);

    private async Task<bool> IsDisconnected(Guid exceptionId, CancellationToken cancellationToken)
    {
        if (exceptionId != ChannelManagementJson.StableId("connection-disconnected")) return false;
        return (await data.Connection(cancellationToken)).IsDisconnected;
    }

    private async Task<ChannelAvailabilityState?> FindAvailabilityState(Guid exceptionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelAvailabilityState> rows;
        try { rows = await data.AvailabilityStates(cancellationToken); }
        catch (ChannelConsoleException) { return null; }
        return rows.FirstOrDefault(row => ChannelManagementJson.StableId(AvailabilityPrefix + row.ProviderItemId) == exceptionId
            && row.State != CataloguePublicationStates.Verified);
    }

    private static object ExceptionWire(ExceptionView row) => new
    {
        id = row.Id,
        kind = row.Kind,
        severity = row.Severity,
        status = row.Status,
        code = row.Code,
        title = row.Title,
        detail = row.Detail,
        createdAt = row.CreatedAt,
        updatedAt = row.UpdatedAt,
        providerOrderId = row.ProviderOrderId,
        localOrderId = row.LocalOrderId,
        canReconcile = row.CanReconcile,
        automaticRetryBlocked = row.AutomaticRetryBlocked
    };

    private static string SafeCode(string value)
        => value.Length is > 0 and <= 48 && value.All(char.IsAsciiLetterOrDigit) ? value : "OperationNeedsReview";

    private static DateTimeOffset? ParseTime(JsonElement value, string name)
        => ChannelManagementJson.NullableTime(value, name);

    private static ExceptionCursor DecodeCursor(string cursor)
    {
        if (cursor.Length == 0) return new(true, null, 0);
        if (cursor.Length > 1024) throw InvalidCursorException();
        try
        {
            var value = JsonSerializer.Deserialize<ExceptionCursor>(WebEncoders.Base64UrlDecode(cursor));
            if (value is null || value.Offset is < 0 or > 1000 || value.ImportCursor?.Length > 350)
                throw InvalidCursorException();
            return value;
        }
        catch (FormatException) { throw InvalidCursorException(); }
        catch (JsonException) { throw InvalidCursorException(); }
    }

    private static ChannelConsoleException InvalidCursorException() => new(400, InvalidCursor);
    private static string EncodeCursor(ExceptionCursor cursor) => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(cursor));

    private sealed record ExceptionCursor(bool IncludeCurrent, string? ImportCursor, int Offset);
    private sealed record ExceptionView(Guid Id, string Kind, string Severity, string Status, string Code, string Title,
        string? Detail, DateTimeOffset CreatedAt, [property: JsonIgnore] Guid? OperationId, Guid? ProviderOrderId,
        Guid? LocalOrderId, bool CanReconcile, bool AutomaticRetryBlocked)
    {
        [JsonIgnore]
        public DateTimeOffset UpdatedAt { get; init; } = CreatedAt;
    }
}
