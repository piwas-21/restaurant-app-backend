using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelAvailabilityService(TenantManagementContext context,
    ITenantChannelAvailabilityState state, IChannelAvailabilityStatus availability,
    ITenantAvailabilityClient tenantAvailability, IChannelManagementAudit audit) : ITenantChannelAvailabilityService
{
    private const string Unknown = "unknown";

    public async Task<JsonElement> Availability(CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var snapshot = await availability.Read(cancellationToken);
        var store = await ActiveStore(cancellationToken);
        if (store is null) return EmptyAvailability(snapshot);

        var sourceItems = await SourceItems(store, cancellationToken);
        var states = AvailabilityStates(snapshot);
        var paused = ProviderJson.Flag(snapshot, "paused");
        var items = store.Items.Select(mapping => AvailabilityItem(mapping, states, sourceItems, paused)).ToArray();
        return ProviderJson.Encode(new
        {
            enabled = ProviderJson.Flag(snapshot, "enabled"),
            paused,
            pausedUntil = ChannelManagementJson.NullableTime(snapshot, "pausedUntil"),
            checkedAt = ChannelManagementJson.NullableTime(snapshot, "checkedAt") ?? context.Clock.GetUtcNow(),
            storeStatus = StoreAvailabilityStatus(paused, ProviderJson.Flag(snapshot, "enabled")),
            items
        });
    }

    public async Task<JsonElement> Pause(TenantManagementPauseRequest request, Guid actorId,
        CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        if (request.DurationMinutes is not (null or 15 or 30 or 60 or 240))
            throw new ChannelConsoleException(400, "Choose a supported pause duration or pause until resumed.");
        var binding = context.Binding();
        await using var lease = await state.TryLease(binding, cancellationToken);
        if (lease is null) throw Busy();
        var now = context.Clock.GetUtcNow();
        var overrideState = await RecordOverride(binding, true, PauseUntil(request.DurationMinutes, now), actorId, now, cancellationToken);
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
        context.RequireEnabled();
        var binding = context.Binding();
        await using var lease = await state.TryLease(binding, cancellationToken);
        if (lease is null) throw Busy();
        var now = context.Clock.GetUtcNow();
        await RecordOverride(binding, false, null, actorId, now, cancellationToken);
        return ProviderJson.Encode(new
        {
            state = "pending",
            effectiveUntil = (DateTimeOffset?)null,
            providerConfirmed = false,
            resultCode = "AvailabilityConfirmationPending"
        });
    }

    private async Task<ChannelAvailabilityOverride> RecordOverride(AvailabilityBinding binding, bool paused,
        DateTimeOffset? until, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var action = paused ? "AvailabilityPause" : "AvailabilityResume";
        await audit.Record(binding, actorId, action, "Intent", null, now, cancellationToken);
        var updated = await state.SetOverride(binding, paused, until, actorId, now, cancellationToken);
        await audit.Record(binding, actorId, action, "Pending", null, context.Clock.GetUtcNow(), cancellationToken);
        return updated;
    }

    private async Task<TenantStoreBinding?> ActiveStore(CancellationToken cancellationToken)
    {
        try { return await state.Active(cancellationToken); }
        catch (ChannelConsoleException)
        {
            // Without an active reviewed mapping, expose an attention state instead of a guessed menu.
            return null;
        }
    }

    private async Task<Dictionary<string, TenantAvailabilityItem>> SourceItems(TenantStoreBinding store,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await tenantAvailability.Read(store, cancellationToken);
            return source.Items.ToDictionary(row => row.ProviderItemId, StringComparer.Ordinal);
        }
        catch (ChannelConsoleException)
        {
            // Current provider observations remain visible when the Sofra stock source is unavailable.
            return new(StringComparer.Ordinal);
        }
    }

    private JsonElement EmptyAvailability(JsonElement snapshot)
        => ProviderJson.Encode(new
        {
            enabled = false,
            paused = ProviderJson.Flag(snapshot, "paused"),
            pausedUntil = ChannelManagementJson.NullableTime(snapshot, "pausedUntil"),
            checkedAt = ChannelManagementJson.NullableTime(snapshot, "checkedAt") ?? context.Clock.GetUtcNow(),
            storeStatus = "needsAttention",
            items = Array.Empty<object>()
        });

    private static Dictionary<string, JsonElement> AvailabilityStates(JsonElement snapshot)
        => snapshot.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToDictionary(row => ProviderJson.Text(row, "itemId"), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private static object AvailabilityItem(TenantItemMapping mapping, Dictionary<string, JsonElement> states,
        Dictionary<string, TenantAvailabilityItem> sourceItems, bool paused)
    {
        var hasState = states.TryGetValue(mapping.ProviderItemId, out var observed);
        sourceItems.TryGetValue(mapping.ProviderItemId, out var desired);
        var state = hasState ? ProviderJson.Text(observed, "state").ToLowerInvariant() : Unknown;
        var desiredAvailable = DesiredAvailable(desired, hasState, observed);
        return new
        {
            providerItemId = mapping.ProviderItemId,
            productId = mapping.ProductId,
            variationId = mapping.VariationId,
            desiredAvailable = !paused && desiredAvailable,
            confirmedAvailable = hasState ? ChannelManagementJson.NullableFlag(observed, "observedAvailable") : null,
            state,
            verifiedAt = hasState ? ChannelManagementJson.NullableTime(observed, "verifiedAt") : null,
            isStale = desired is null || !hasState || !IsFresh(observed),
            reasonCode = paused ? "ManagerPaused" : desired?.Reason
        };
    }

    private static bool IsFresh(JsonElement state)
        => state.TryGetProperty("fresh", out var fresh) && fresh.ValueKind == JsonValueKind.True;

    private static bool DesiredAvailable(TenantAvailabilityItem? source, bool hasState, JsonElement observed)
    {
        if (source is not null) return source.Available;
        return hasState && ProviderJson.Flag(observed, "desiredAvailable");
    }

    private static DateTimeOffset? PauseUntil(int? durationMinutes, DateTimeOffset now)
        => durationMinutes is { } duration ? now.AddMinutes(duration) : null;

    private static string StoreAvailabilityStatus(bool paused, bool enabled)
    {
        if (paused) return "paused";
        return enabled ? "active" : "needsAttention";
    }

    private static ChannelConsoleException Busy()
        => new(409, "A channel operation is in progress. Reload before changing availability.");
}
