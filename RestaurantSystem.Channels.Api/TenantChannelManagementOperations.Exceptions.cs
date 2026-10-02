using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations
{
    private const int ExceptionPageSize = 50;

    public async Task<JsonElement> Exceptions(string cursor, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var page = DecodeExceptionCursor(cursor);
        var imports = await _imports.Read(page.ImportCursor ?? string.Empty, cancellationToken);
        var rows = new List<ExceptionView>();
        if (page.IncludeCurrent)
        {
            rows.AddRange(await PublicationExceptions(cancellationToken));
            rows.AddRange(await AvailabilityExceptions(cancellationToken));
            rows.AddRange(await ImportExceptions(imports, cancellationToken));
            rows.AddRange(await AuditExceptions(cancellationToken));
        }
        else rows.AddRange(await ImportExceptions(imports, cancellationToken));
        var ordered = rows.DistinctBy(row => row.Id).OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Id).ToArray();
        var items = ordered.Skip(page.Offset).Take(ExceptionPageSize).ToArray();
        string? next = null;
        if (page.Offset + items.Length < ordered.Length)
            next = EncodeExceptionCursor(page with { Offset = page.Offset + items.Length });
        else if (ProviderJson.Flag(imports, "truncated") && ProviderJson.Text(imports, "nextCursor") is { Length: > 0 } importCursor)
            next = EncodeExceptionCursor(new(false, importCursor, 0));
        return ProviderJson.Encode(new { items = items.Select(ExceptionWire), nextCursor = next, checkedAt = _clock.GetUtcNow() });
    }

    public async Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken)
    {
        var cursor = string.Empty;
        do
        {
            var rows = await Exceptions(cursor, cancellationToken);
            if (rows.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    if (Guid.TryParse(ProviderJson.Text(item, "id"), out var id) && id == exceptionId) return item.Clone();
            cursor = rows.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() ?? string.Empty : string.Empty;
        } while (cursor.Length > 0);
        throw new ChannelConsoleException(404, "Exception was not found.");
    }

    public async Task<JsonElement> Reconcile(Guid exceptionId, Guid actorId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var publication = await _publications.Find(Binding(), exceptionId, cancellationToken);
        if (publication is not null)
            return await ReconcilePublication(publication, actorId, cancellationToken);
        var state = await _connectionState.Read(Binding(), cancellationToken);
        if (state.IsDisconnected && exceptionId == StableId("connection-disconnected"))
            return Result(exceptionId, "open", "IntegrationDisconnected", false);
        var availabilityState = await FindAvailabilityState(exceptionId, cancellationToken);
        if (availabilityState is not null)
            return await ReconcileAvailability(availabilityState, actorId, cancellationToken);
        return ProviderJson.Encode(new
        {
            operationId = exceptionId,
            status = "open",
            code = "ReviewRequired",
            observedAt = (DateTimeOffset?)null,
            providerRequestSent = false
        });
    }

    private async Task<JsonElement> ReconcilePublication(CataloguePublication row, Guid actorId,
        CancellationToken cancellationToken)
    {
        if (row.State == CataloguePublicationStates.Verified) return Result(row.Id, "resolved", null, false);
        if (row.State != CataloguePublicationStates.Pending)
            return Result(row.Id, "open", "PublicationNoLongerPending", false);
        var revision = row.MappingSnapshot is { } snapshot ? ProviderJson.Text(snapshot, "catalogueRevision") : string.Empty;
        if (revision.Length == 0) return Result(row.Id, "uncertain", "MappingSnapshotUnavailable", false);
        var binding = new AvailabilityBinding(_webhook.Value.ClientId, ConfiguredStore.StoreId, ConfiguredStore.TenantId, revision);
        var current = await _publications.Latest(binding, cancellationToken);
        if (current?.Id != row.Id) return Result(row.Id, "uncertain", "A newer publication requires review", false);
        await _audit.Record(binding, actorId, "PublicationReconcile", "Intent", row.Id, _clock.GetUtcNow(), cancellationToken);
        var actual = await _menu.Read(cancellationToken);
        var now = _clock.GetUtcNow();
        if (Matches(row.Menu, actual))
        {
            var verified = await _publications.Verify(binding, row.Id, ProviderJson.Hash(actual), now, cancellationToken);
            await _audit.Record(binding, actorId, "PublicationReconcile", verified ? "Verified" : "Unconfirmed", row.Id, now, cancellationToken);
            return Result(row.Id, verified ? "resolved" : "uncertain", verified ? null : "PublicationUnconfirmed", false, now);
        }
        if (Matches(row.PreviousMenu, actual))
        {
            var abandoned = await _publications.Abandon(binding, row.Id, cancellationToken);
            await _audit.Record(binding, actorId, "PublicationReconcile", abandoned ? "Abandoned" : "Unconfirmed", row.Id, now, cancellationToken);
            return Result(row.Id, abandoned ? "resolved" : "uncertain", abandoned ? "PreviousMenuConfirmed" : "PublicationUnconfirmed", false, now);
        }
        await _audit.Record(binding, actorId, "PublicationReconcile", "Unconfirmed", row.Id, now, cancellationToken);
        return Result(row.Id, "uncertain", "ProviderMenuDiffersFromExpectedAndPrevious", false, now);
    }

    private async Task<JsonElement> ReconcileAvailability(ChannelAvailabilityState state, Guid actorId,
        CancellationToken cancellationToken)
    {
        var store = await _mappingResolver.Active(cancellationToken); var binding = Binding(store);
        var source = await _tenantAvailability.Read(store, cancellationToken);
        var intent = await _availabilityOverrides.Read(binding, cancellationToken);
        var paused = intent is { IsPaused: true } && (intent.PausedUntil is null || intent.PausedUntil > _clock.GetUtcNow());
        var revision = intent is null ? source.Revision : ProviderJson.Hash(ProviderJson.Encode(new
        { sourceRevision = source.Revision, paused, updatedAt = intent.UpdatedAt, pausedUntil = intent.PausedUntil }));
        if (revision != state.SourceRevision)
            return Result(StableId("availability:" + state.ProviderItemId), "open", "AvailabilityIntentChanged", false);
        await using var lease = await _availabilityJobs.TryLease(binding, cancellationToken);
        if (lease is null) return Result(StableId("availability:" + state.ProviderItemId), "reconciling", "AvailabilitySyncInProgress", false);
        var operationId = StableId("availability:" + state.ProviderItemId);
        await _audit.Record(binding, actorId, "AvailabilityReconcile", "Intent", operationId,
            _clock.GetUtcNow(), cancellationToken);
        var actual = await _uberAvailability.Read(store, _webhook.Value.ClientId, cancellationToken);
        var observed = actual.Items[state.ProviderItemId]; var now = _clock.GetUtcNow();
        var sourceItem = source.Items.SingleOrDefault(item => item.ProviderItemId == state.ProviderItemId);
        if (sourceItem is null) return Result(StableId("availability:" + state.ProviderItemId), "open", "AvailabilitySourceUnavailable", false);
        var matches = observed == (paused ? false : sourceItem.Available);
        if (matches) await lease.Observe(state.ProviderItemId, state.SourceRevision, "Verified", observed, actual.Hash, now, cancellationToken);
        else await lease.Observe(state.ProviderItemId, state.SourceRevision, "Mismatch", observed, actual.Hash, now, cancellationToken);
        await _audit.Record(binding, actorId, "AvailabilityReconcile", matches ? "Verified" : "Mismatch",
            operationId, now, cancellationToken);
        return Result(StableId("availability:" + state.ProviderItemId), matches ? "resolved" : "open",
            matches ? null : "ProviderAvailabilityMismatch", false, now);
    }

    private async Task<ChannelAvailabilityState?> FindAvailabilityState(Guid exceptionId, CancellationToken cancellationToken)
    {
        TenantStoreBinding store;
        try { store = await _mappingResolver.Active(cancellationToken); }
        catch (ChannelConsoleException) { return null; }
        var states = await _availabilityJobs.Read(Binding(store), cancellationToken);
        return states.FirstOrDefault(item => StableId("availability:" + item.ProviderItemId) == exceptionId
            && item.State != "Verified");
    }

    private static bool Matches(JsonElement expected, JsonElement actual)
    {
        try { SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(expected), actual); return true; }
        catch (ChannelConsoleException) { return false; }
    }

    private static JsonElement Result(Guid id, string status, string? code, bool requestSent, DateTimeOffset? observedAt = null)
        => ProviderJson.Encode(new { operationId = id, status, code, observedAt, providerRequestSent = requestSent });

    private static Guid StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task<IReadOnlyList<ExceptionView>> PublicationExceptions(CancellationToken cancellationToken)
    {
        var latest = await _publications.Latest(Binding(), cancellationToken);
        if (latest is not { State: CataloguePublicationStates.Pending }) return [];
        return [new(latest.Id, "publication", "warning", "reconciling", "ProviderReadbackPending",
            "Menu publication needs readback", "Uber has not confirmed the published menu yet.", latest.VerifiedAt ?? _clock.GetUtcNow(),
            latest.Id, null, null, true, true)];
    }

    private async Task<IReadOnlyList<ExceptionView>> AvailabilityExceptions(CancellationToken cancellationToken)
    {
        TenantStoreBinding store;
        try { store = await _mappingResolver.Active(cancellationToken); }
        catch (ChannelConsoleException) { return []; }
        var rows = await _availabilityJobs.Read(Binding(store), cancellationToken);
        return rows.Where(row => row.State != "Verified").Select(row => new ExceptionView(StableId("availability:" + row.ProviderItemId),
            "availability", "warning", row.State == "Uncertain" ? "reconciling" : "open", "Availability" + row.State,
            "Uber item availability is not confirmed", "Refresh the provider readback before taking action.",
            row.VerifiedAt ?? _clock.GetUtcNow(), null, null, null, true, true)).ToArray();
    }

    private Task<IReadOnlyList<ExceptionView>> ImportExceptions(JsonElement imports, CancellationToken cancellationToken)
    {
        if (!imports.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return Task.FromResult<IReadOnlyList<ExceptionView>>([]);
        var result = new List<ExceptionView>();
        foreach (var row in rows.EnumerateArray())
        {
            var state = ProviderJson.Text(row, "state"); var code = ProviderJson.Text(row, "code");
            if (state == "Imported" || code.Length == 0 && state != "Quarantined") continue;
            var rawId = ProviderJson.Text(row, "orderId");
            Guid? providerId = Guid.TryParse(rawId, out var parsed) ? parsed : null;
            Guid? localId = Guid.TryParse(ProviderJson.Text(row, "tenantOrderId"), out var local) ? local : null;
            var id = providerId ?? StableId("import:" + rawId);
            var updated = ParseTime(row, "updatedAt") ?? _clock.GetUtcNow();
            result.Add(new(id, "import", state == "Quarantined" ? "critical" : "warning",
                state == "Quarantined" ? "open" : "reconciling", SafeCode(code), "Uber order needs review",
                "The order is held for operator review. No payment was collected by Sofra.", ParseTime(row, "createdAt") ?? updated,
                id, providerId, localId, false, true)
            { UpdatedAt = updated });
        }
        return Task.FromResult<IReadOnlyList<ExceptionView>>(result);
    }

    private async Task<IReadOnlyList<ExceptionView>> AuditExceptions(CancellationToken cancellationToken)
    {
        var records = await _audit.Read(Binding(), null, ExceptionPageSize, cancellationToken);
        var rows = new List<ExceptionView>();
        var latestOAuth = records.FirstOrDefault(row => row.Action is "OAuthStart" or "OAuth");
        if (latestOAuth is { Action: "OAuth", OperationId: { } flowId })
        {
            var flow = await _oauthFlows.Read(Binding(), flowId, cancellationToken);
            if (flow is { Status: "Failed" or "Expired" })
                rows.Add(new(flow.Id, "connection", "warning", "open", SafeCode(flow.ErrorCode ?? "ConnectionUnconfirmed"),
                    "Uber authorization needs attention", "Reconnect to the approved store and review the confirmed connection status.",
                    flow.CreatedAt, flow.Id, null, null, false, true)
                { UpdatedAt = flow.CompletedAt ?? flow.CreatedAt });
        }
        var connection = await _connectionState.Read(Binding(), cancellationToken);
        if (connection.IsDisconnected)
            rows.Add(new(StableId("connection-disconnected"), "connection", "warning", "open", "IntegrationDisconnected",
                "Uber connection is disconnected", "Reconnect and confirm the approved store before accepting new orders.",
                connection.UpdatedAt ?? _clock.GetUtcNow(), null, null, null, false, true));
        return rows;
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
        => value.TryGetProperty(name, out var field) && field.TryGetDateTimeOffset(out var time) ? time : null;

    private static ExceptionCursor DecodeExceptionCursor(string cursor)
    {
        if (cursor.Length == 0) return new(true, null, 0);
        if (cursor.Length > 1024) throw new ChannelConsoleException(400, "Refresh the exception inbox for a valid page.");
        try
        {
            var value = JsonSerializer.Deserialize<ExceptionCursor>(WebEncoders.Base64UrlDecode(cursor));
            if (value is null || value.Offset is < 0 or > 1000 || value.ImportCursor?.Length > 350)
                throw new ChannelConsoleException(400, "Refresh the exception inbox for a valid page.");
            return value;
        }
        catch (FormatException) { throw new ChannelConsoleException(400, "Refresh the exception inbox for a valid page."); }
        catch (JsonException) { throw new ChannelConsoleException(400, "Refresh the exception inbox for a valid page."); }
    }

    private static string EncodeExceptionCursor(ExceptionCursor cursor)
        => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(cursor));

    private sealed record ExceptionCursor(bool IncludeCurrent, string? ImportCursor, int Offset);
    private sealed record ExceptionView(Guid Id, string Kind, string Severity, string Status, string Code, string Title,
        string? Detail, DateTimeOffset CreatedAt, [property: JsonIgnore] Guid? OperationId, Guid? ProviderOrderId, Guid? LocalOrderId,
        bool CanReconcile, bool AutomaticRetryBlocked)
    {
        [JsonIgnore]
        public DateTimeOffset UpdatedAt { get; init; } = CreatedAt;
    }
}
