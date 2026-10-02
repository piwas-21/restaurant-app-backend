using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantCataloguePublication(TenantManagementContext context, ITenantCatalogueClient tenant,
    ICataloguePublications repository, IChannelAvailabilityJobs leases, ITenantMenuProvider provider,
    ICatalogueMappingResolver? mappings = null) : ITenantCataloguePublication
{
    private TenantStoreBinding Store => context.ConfiguredStore;
    private AvailabilityBinding Binding(TenantStoreBinding store) => context.Binding(store);

    public async Task<JsonElement> Preview(JsonElement template, CancellationToken cancellationToken)
        => await Preview(template, await Active(cancellationToken), cancellationToken);

    public async Task<JsonElement> Preview(JsonElement template, TenantStoreBinding store, CancellationToken cancellationToken)
    {
        RequireStore(store); var plan = CatalogueMenuPlanner.Build(template, store, await tenant.Read(store, cancellationToken));
        var latest = await repository.Latest(Binding(store), cancellationToken);
        return ProviderJson.Encode(new
        {
            canPublish = plan.CanPublish,
            revision = plan.Revision,
            sourceRevision = plan.SourceRevision,
            mappingRevision = store.CatalogueRevision,
            storeId = store.StoreId,
            tenantId = store.TenantId,
            items = plan.Items.Select(item => new
            {
                itemId = item.ItemId,
                productId = item.ProductId,
                variationId = item.VariationId,
                name = item.Name,
                variationName = item.VariationName,
                priceMinor = item.PriceMinor,
                available = item.Available,
                blockReason = item.BlockReason
            }),
            menu = plan.CanPublish ? (JsonElement?)plan.Menu : null,
            publicationState = latest?.State ?? "NotPublished",
            verifiedAt = latest?.VerifiedAt
        });
    }

    public async Task<JsonElement> Publish(JsonElement template, string revision, CancellationToken cancellationToken)
        => await Publish(template, revision, await Active(cancellationToken), cancellationToken);

    public async Task<JsonElement> Publish(JsonElement template, string revision, TenantStoreBinding store, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? intentStillCurrent = null)
    {
        RequireStore(store);
        var binding = Binding(store);
        await using var lease = await leases.TryLease(binding, cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "Stock reconciliation is in progress. Refresh the preview before publishing.");
        await RequireCurrentIntent(intentStillCurrent, cancellationToken);
        var plan = await CurrentPlan(template, store, revision, cancellationToken);
        var readback = await CurrentPublication(binding, plan, revision, cancellationToken);
        var latest = readback.Latest;
        var actual = readback.Actual;
        var matches = Matches(plan.Menu, actual);
        var baseline = Baseline(latest, template);
        if (!matches) RequirePreviousMenu(baseline, actual);
        latest = await BeginIfNeeded(store, latest, plan, baseline, cancellationToken);
        actual = matches ? actual : await UploadReviewedMenu(store, plan, intentStillCurrent, cancellationToken);
        SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(plan.Menu), actual);
        if (!await repository.Verify(binding, latest.Id, ProviderJson.Hash(actual), context.Clock.GetUtcNow(), cancellationToken)) throw Unconfirmed();
        return ProviderJson.Encode(new { verified = true, revision = plan.Revision, sourceRevision = plan.SourceRevision, mappingRevision = store.CatalogueRevision });
    }

    private async Task<CatalogueMenuPlan> CurrentPlan(JsonElement template, TenantStoreBinding store, string revision,
        CancellationToken cancellationToken)
    {
        var plan = CatalogueMenuPlanner.Build(template, store, await tenant.Read(store, cancellationToken));
        if (!plan.CanPublish || plan.Revision != revision || revision.Length != TenantCatalogueLimits.RevisionLength)
            throw new ChannelConsoleException(409, "The tenant catalogue changed or has blocked selections. Refresh and review the complete preview.");
        return plan;
    }

    private async Task<(CataloguePublication? Latest, JsonElement Actual)> CurrentPublication(AvailabilityBinding binding,
        CatalogueMenuPlan plan, string revision, CancellationToken cancellationToken)
    {
        var latest = await repository.Latest(binding, cancellationToken);
        if (latest is { State: CataloguePublicationStates.Pending } && latest.MappingHash != plan.MappingHash) throw Unconfirmed();
        var actual = await provider.Read(cancellationToken);
        latest = await ResolveStalePending(binding, latest, revision, actual, cancellationToken);
        return (latest, actual);
    }

    private async Task<JsonElement> UploadReviewedMenu(TenantStoreBinding store, CatalogueMenuPlan plan,
        Func<CancellationToken, Task<bool>>? intentStillCurrent, CancellationToken cancellationToken)
    {
        if ((await tenant.Read(store, cancellationToken)).Revision != plan.SourceRevision) throw Unconfirmed();
        await RequireCurrentIntent(intentStillCurrent, cancellationToken);
        await provider.Upload(plan.Menu, cancellationToken);
        return await provider.Read(cancellationToken);
    }

    private static async Task RequireCurrentIntent(Func<CancellationToken, Task<bool>>? intentStillCurrent,
        CancellationToken cancellationToken)
    {
        if (intentStillCurrent is not null && !await intentStillCurrent(cancellationToken)) throw Unconfirmed();
    }

    private static JsonElement Baseline(CataloguePublication? latest, JsonElement template)
        => latest?.State switch
        {
            CataloguePublicationStates.Pending or CataloguePublicationStates.Abandoned => latest.PreviousMenu,
            CataloguePublicationStates.Verified => latest.Menu,
            _ => template
        };

    private static void RequirePreviousMenu(JsonElement baseline, JsonElement actual)
        => SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(baseline), actual);

    public async Task<JsonElement> Expected(CancellationToken cancellationToken)
    {
        RequireEnabled(); var store = await Active(cancellationToken);
        var latest = await repository.Latest(Binding(store), cancellationToken);
        if (latest is not { State: CataloguePublicationStates.Verified } || latest.MappingHash != CatalogueMenuPlanner.MappingHash(store)) throw Unconfirmed();
        return CatalogueMenuPlanner.Structural(latest.Menu);
    }

    public async Task RequireActive(CancellationToken cancellationToken) => _ = await Expected(cancellationToken);

    private async Task<CataloguePublication> BeginIfNeeded(TenantStoreBinding store, CataloguePublication? latest, CatalogueMenuPlan plan,
        JsonElement baseline, CancellationToken cancellationToken)
    {
        if (latest is not { State: CataloguePublicationStates.Pending }
            && !(latest is { State: CataloguePublicationStates.Verified } && latest.Revision == plan.Revision))
            latest = await repository.Begin(Binding(store), new CataloguePublicationIntent(
                plan.MappingHash, plan.SourceRevision, plan.Revision, plan.Menu, baseline, ProviderJson.Encode(new
                {
                    catalogueRevision = store.CatalogueRevision,
                    items = store.Items.Select(item => new
                    { providerItemId = item.ProviderItemId, productId = item.ProductId, variationId = item.VariationId, variationName = item.VariationName })
                })), cancellationToken);
        return latest ?? throw Unconfirmed();
    }

    private async Task<CataloguePublication?> ResolveStalePending(AvailabilityBinding binding, CataloguePublication? latest, string revision,
        JsonElement actual, CancellationToken cancellationToken)
    {
        if (latest is not { State: CataloguePublicationStates.Pending } || latest.Revision == revision) return latest;
        if (Matches(latest.Menu, actual))
        {
            if (!await repository.Verify(binding, latest.Id, ProviderJson.Hash(actual), context.Clock.GetUtcNow(), cancellationToken)) throw Unconfirmed();
            return latest with { State = CataloguePublicationStates.Verified };
        }
        // A changed source may supersede an unsent draft only after independent proof the previous menu remains.
        SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(latest.PreviousMenu), actual);
        if (!await repository.Abandon(binding, latest.Id, cancellationToken)) throw Unconfirmed();
        return latest with { State = CataloguePublicationStates.Abandoned };
    }

    private Task<TenantStoreBinding> Active(CancellationToken cancellationToken)
        => mappings is null ? Task.FromResult(Store) : mappings.Active(cancellationToken);

    private void RequireStore(TenantStoreBinding store)
    {
        RequireEnabled();
        if (store.StoreId != Store.StoreId || store.TenantId != Store.TenantId || store.BaseUrl != Store.BaseUrl
            || store.Currency != Store.Currency || store.ApiToken != Store.ApiToken || store.CatalogueApiToken != Store.CatalogueApiToken
            || store.CatalogueRevision.Length is < 1 or > 128 || store.CatalogueRevision.Any(char.IsControl)) throw Unconfirmed();
    }

    private void RequireEnabled()
    {
        if (!context.Bridge.Enabled || !context.Bridge.UseTenantCatalogue || Store.CatalogueApiToken.Length == 0
            || context.Webhook.StoreIds.Length != 1 || context.Webhook.StoreIds[0] != Store.StoreId) throw Unconfirmed();
    }

    private static bool Matches(JsonElement expected, JsonElement actual)
    {
        try { SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(expected), actual); return true; }
        catch (ChannelConsoleException) { return false; }
    }

    private static ChannelConsoleException Unconfirmed() => new(409, "Tenant menu publication is unconfirmed. Review the source preview and current Uber menu before continuing.");
}
