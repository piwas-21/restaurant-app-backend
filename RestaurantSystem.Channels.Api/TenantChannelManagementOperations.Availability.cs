using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations
{
    public async Task<JsonElement> Availability(CancellationToken cancellationToken)
    {
        RequireEnabled();
        var snapshot = await _availability.Read(cancellationToken);
        TenantStoreBinding? store = null;
        try { store = await _mappingResolver.Active(cancellationToken); }
        catch (ChannelConsoleException) { }
        if (store is null)
            return ProviderJson.Encode(new
            {
                enabled = false,
                paused = ProviderJson.Flag(snapshot, "paused"),
                pausedUntil = NullableTime(snapshot, "pausedUntil"),
                checkedAt = NullableTime(snapshot, "checkedAt") ?? _clock.GetUtcNow(),
                storeStatus = "needsAttention",
                items = Array.Empty<object>()
            });
        TenantAvailabilitySnapshot? source = null;
        try { source = await _tenantAvailability.Read(store, cancellationToken); }
        catch (ChannelConsoleException) { }
        var states = snapshot.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToDictionary(row => ProviderJson.Text(row, "itemId"), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var sourceItems = source?.Items.ToDictionary(row => row.ProviderItemId, StringComparer.Ordinal)
            ?? new Dictionary<string, TenantAvailabilityItem>(StringComparer.Ordinal);
        var paused = ProviderJson.Flag(snapshot, "paused");
        var items = store.Items.Select(mapping =>
        {
            var hasState = states.TryGetValue(mapping.ProviderItemId, out var state);
            sourceItems.TryGetValue(mapping.ProviderItemId, out var desired);
            return new
            {
                providerItemId = mapping.ProviderItemId,
                productId = mapping.ProductId,
                variationId = mapping.VariationId,
                desiredAvailable = !paused && (desired?.Available ?? (hasState && ProviderJson.Flag(state, "desiredAvailable"))),
                confirmedAvailable = hasState ? NullableFlag(state, "observedAvailable") : null,
                state = hasState ? ProviderJson.Text(state, "state").ToLowerInvariant() : "unknown",
                verifiedAt = hasState ? NullableTime(state, "verifiedAt") : null,
                isStale = desired is null || !hasState || !state.TryGetProperty("fresh", out var fresh) || fresh.ValueKind != JsonValueKind.True,
                reasonCode = paused ? "ManagerPaused" : desired?.Reason
            };
        }).ToArray();
        return ProviderJson.Encode(new
        {
            enabled = ProviderJson.Flag(snapshot, "enabled"),
            paused,
            pausedUntil = NullableTime(snapshot, "pausedUntil"),
            checkedAt = NullableTime(snapshot, "checkedAt") ?? _clock.GetUtcNow(),
            storeStatus = paused ? "paused" : ProviderJson.Flag(snapshot, "enabled") ? "active" : "needsAttention",
            items
        });
    }

    public async Task<JsonElement> Pause(TenantManagementPauseRequest request, Guid actorId,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (request.DurationMinutes is not (null or 15 or 30 or 60 or 240))
            throw new ChannelConsoleException(400, "Choose a supported pause duration or pause until resumed.");
        await using var lease = await _availabilityJobs.TryLease(Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A channel operation is in progress. Reload before changing availability.");
        var now = _clock.GetUtcNow(); DateTimeOffset? until = request.DurationMinutes is { } duration ? now.AddMinutes(duration) : null;
        await _audit.Record(Binding(), actorId, "AvailabilityPause", "Intent", null, now, cancellationToken);
        var overrideState = await _availabilityOverrides.Set(Binding(), true, until, actorId, now, cancellationToken);
        await _audit.Record(Binding(), actorId, "AvailabilityPause", "Pending", null, _clock.GetUtcNow(), cancellationToken);
        return ProviderJson.Encode(new
        {
            state = "pending",
            effectiveUntil = overrideState.PausedUntil,
            providerConfirmed = false,
            resultCode = "AvailabilityConfirmationPending"
        });
    }

    public async Task<JsonElement> Resume(Guid actorId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        await using var lease = await _availabilityJobs.TryLease(Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A channel operation is in progress. Reload before changing availability.");
        var now = _clock.GetUtcNow();
        await _audit.Record(Binding(), actorId, "AvailabilityResume", "Intent", null, now, cancellationToken);
        await _availabilityOverrides.Set(Binding(), false, null, actorId, now, cancellationToken);
        await _audit.Record(Binding(), actorId, "AvailabilityResume", "Pending", null, _clock.GetUtcNow(), cancellationToken);
        return ProviderJson.Encode(new
        {
            state = "pending",
            effectiveUntil = (DateTimeOffset?)null,
            providerConfirmed = false,
            resultCode = "AvailabilityConfirmationPending"
        });
    }

    public async Task<JsonElement> Disconnect(Guid storeId, Guid actorId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (storeId != ConfiguredStore.StoreId) throw new ChannelConsoleException(404, "The approved Uber store was not found.");
        await using var lease = await _availabilityJobs.TryLease(Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A channel operation is in progress. Reload before disconnecting.");
        var now = _clock.GetUtcNow();
        await _audit.Record(Binding(), actorId, "Disconnect", "Intent", null, now, cancellationToken);
        await _oauthFlows.CancelPending(Binding(), now, cancellationToken);
        await _connectionState.Set(Binding(), true, actorId, now, cancellationToken);
        await _availabilityOverrides.Set(Binding(), true, null, actorId, now, cancellationToken);
        try
        {
            var actual = await _connection.EnableOrders(false, cancellationToken);
            var relinquished = !ProviderJson.Flag(actual, "enabled") && !ProviderJson.Flag(actual, "orderManager")
                && !ProviderJson.Flag(actual, "pending");
            var result = ProviderJson.Encode(new
            {
                status = relinquished ? "disconnected" : "uncertain",
                providerManagerRelinquished = relinquished,
                localBridgePaused = true,
                resultCode = relinquished ? null : "ProviderRevocationUnconfirmed",
                completedAt = _clock.GetUtcNow()
            });
            await _audit.Record(Binding(), actorId, "Disconnect", relinquished ? "Confirmed" : "Unconfirmed",
                null, _clock.GetUtcNow(), cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is ChannelConsoleException or HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            await _audit.Record(Binding(), actorId, "Disconnect", "Unconfirmed", null, _clock.GetUtcNow(), cancellationToken);
            return ProviderJson.Encode(new
            {
                status = "uncertain",
                providerManagerRelinquished = false,
                localBridgePaused = true,
                resultCode = "ProviderRevocationUnconfirmed",
                completedAt = _clock.GetUtcNow()
            });
        }
    }

    private static bool? NullableFlag(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? field.GetBoolean() : null;

    private static DateTimeOffset? NullableTime(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            && field.TryGetDateTimeOffset(out var date) ? date : null;
}
