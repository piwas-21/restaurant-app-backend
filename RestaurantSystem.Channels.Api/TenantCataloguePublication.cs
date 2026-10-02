using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantCataloguePublication(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ITenantCatalogueClient tenant, ICataloguePublications repository, IChannelAvailabilityJobs leases,
    ITenantMenuProvider provider, TimeProvider clock) : ITenantCataloguePublication
{
    private TenantStoreBinding Store => settings.Value.Store;
    private AvailabilityBinding Binding => new(webhook.Value.ClientId, Store.StoreId, Store.TenantId, Store.CatalogueRevision);

    public async Task<JsonElement> Preview(JsonElement template, CancellationToken cancellationToken)
    {
        RequireEnabled(); var plan = CatalogueMenuPlanner.Build(template, Store, await tenant.Read(Store, cancellationToken));
        var latest = await repository.Latest(Binding, cancellationToken);
        return ProviderJson.Encode(new
        {
            canPublish = plan.CanPublish,
            revision = plan.Revision,
            sourceRevision = plan.SourceRevision,
            mappingRevision = Store.CatalogueRevision,
            storeId = Store.StoreId,
            tenantId = Store.TenantId,
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
    {
        RequireEnabled();
        await using var lease = await leases.TryLease(Binding, cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "Stock reconciliation is in progress. Refresh the preview before publishing.");
        var plan = CatalogueMenuPlanner.Build(template, Store, await tenant.Read(Store, cancellationToken));
        if (!plan.CanPublish || plan.Revision != revision || revision.Length != TenantCatalogueLimits.RevisionLength)
            throw new ChannelConsoleException(409, "The tenant catalogue changed or has blocked selections. Refresh and review the complete preview.");
        var latest = await repository.Latest(Binding, cancellationToken);
        if (latest is { State: CataloguePublicationStates.Pending } && latest.MappingHash != plan.MappingHash) throw Unconfirmed();
        var actual = await provider.Read(cancellationToken);
        latest = await ResolveStalePending(latest, revision, actual, cancellationToken);
        var matches = Matches(plan.Menu, actual);
        var baseline = latest is { State: CataloguePublicationStates.Pending or CataloguePublicationStates.Abandoned } ? latest.PreviousMenu : latest?.Menu ?? template;
        if (!matches) SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(baseline), actual);
        latest = await BeginIfNeeded(latest, plan, baseline, cancellationToken);
        if (!matches)
        {
            // Source intent is checked again immediately before the full replacement.
            if ((await tenant.Read(Store, cancellationToken)).Revision != plan.SourceRevision) throw Unconfirmed();
            await provider.Upload(plan.Menu, cancellationToken);
            actual = await provider.Read(cancellationToken);
        }
        SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(plan.Menu), actual);
        if (!await repository.Verify(Binding, latest.Id, ProviderJson.Hash(actual), clock.GetUtcNow(), cancellationToken)) throw Unconfirmed();
        return ProviderJson.Encode(new { verified = true, revision = plan.Revision, sourceRevision = plan.SourceRevision, mappingRevision = Store.CatalogueRevision });
    }

    public async Task<JsonElement> Expected(CancellationToken cancellationToken)
    {
        RequireEnabled(); var latest = await repository.Latest(Binding, cancellationToken);
        if (latest is not { State: CataloguePublicationStates.Verified } || latest.MappingHash != CatalogueMenuPlanner.MappingHash(Store)) throw Unconfirmed();
        return CatalogueMenuPlanner.Structural(latest.Menu);
    }

    public async Task RequireActive(CancellationToken cancellationToken) => _ = await Expected(cancellationToken);

    private async Task<CataloguePublication> BeginIfNeeded(CataloguePublication? latest, CatalogueMenuPlan plan,
        JsonElement baseline, CancellationToken cancellationToken)
    {
        if (latest is not { State: CataloguePublicationStates.Pending }
            && !(latest is { State: CataloguePublicationStates.Verified } && latest.Revision == plan.Revision))
            latest = await repository.Begin(Binding, plan.MappingHash, plan.SourceRevision, plan.Revision, plan.Menu, baseline, cancellationToken);
        return latest ?? throw Unconfirmed();
    }

    private async Task<CataloguePublication?> ResolveStalePending(CataloguePublication? latest, string revision,
        JsonElement actual, CancellationToken cancellationToken)
    {
        if (latest is not { State: CataloguePublicationStates.Pending } || latest.Revision == revision) return latest;
        if (Matches(latest.Menu, actual))
        {
            if (!await repository.Verify(Binding, latest.Id, ProviderJson.Hash(actual), clock.GetUtcNow(), cancellationToken)) throw Unconfirmed();
            return latest with { State = CataloguePublicationStates.Verified };
        }
        // A changed source may supersede an unsent draft only after independent proof the previous menu remains.
        SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(latest.PreviousMenu), actual);
        if (!await repository.Abandon(Binding, latest.Id, cancellationToken)) throw Unconfirmed();
        return latest with { State = CataloguePublicationStates.Abandoned };
    }

    private void RequireEnabled()
    {
        if (!settings.Value.Enabled || !settings.Value.UseTenantCatalogue || Store.CatalogueApiToken.Length == 0
            || webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != Store.StoreId) throw Unconfirmed();
    }

    private static bool Matches(JsonElement expected, JsonElement actual)
    {
        try { SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(expected), actual); return true; }
        catch (ChannelConsoleException) { return false; }
    }

    private static ChannelConsoleException Unconfirmed() => new(409, "Tenant menu publication is unconfirmed. Review the source preview and current Uber menu before continuing.");
}
