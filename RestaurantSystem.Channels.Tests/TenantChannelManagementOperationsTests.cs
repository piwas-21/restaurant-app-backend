using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantChannelManagementOperationsTests
{
    private static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task FirstSaveCanReloadPreviewAndPublishWithoutAnExistingDraft()
    {
        var operations = CreateOperations();

        var saved = await operations.SaveDraft(new(null, [new("meal", ProductId, null)]), ActorId, default);
        var draftRevision = saved.GetProperty("draftRevision").GetString();
        Assert.True(Guid.TryParse(draftRevision, out _));
        Assert.Equal("currentReadback", saved.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());

        var catalogueView = await operations.Catalogue(default);
        Assert.Equal(draftRevision, catalogueView.GetProperty("draftRevision").GetString());
        Assert.Equal("currentReadback", catalogueView.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());
        Assert.Equal("reviewedTemplate", catalogueView.GetProperty("serviceHoursStatus").GetString());
        Assert.Equal("currentReadback", catalogueView.GetProperty("currentServiceHoursStatus").GetString());
        var preview = await operations.Preview(new(draftRevision!), ActorId, default);
        Assert.True(preview.GetProperty("canPublish").GetBoolean());
        Assert.False(preview.GetProperty("serviceHoursEditable").GetBoolean());

        var result = await operations.Publish(new(draftRevision!, preview.GetProperty("publicationRevision").GetString()!), ActorId, default);
        Assert.Equal("verified", result.GetProperty("state").GetString());
        Assert.True(result.GetProperty("providerReadbackVerified").GetBoolean());
        var publishedCatalogue = await operations.Catalogue(default);
        Assert.Equal("currentReadback", publishedCatalogue.GetProperty("items")[0].GetProperty("providerPriceStatus").GetString());
        Assert.Equal(725, publishedCatalogue.GetProperty("items")[0].GetProperty("providerPriceMinor").GetInt32());
    }

    [Fact]
    public async Task CategoryPublishRequiresExplicitTaxProfileConfirmation()
    {
        var harness = CreateHarness(categorySelectionEnabled: true, providerMenu: CategoryMenu());
        var source = await harness.Operations.CatalogueCategories(default);
        var saved = await harness.Operations.SaveDraft(new(source.GetProperty("draftRevision").GetString(), [])
        {
            ExpectedSourceRevision = source.GetProperty("sourceRevision").GetString()!,
            CategoryIds = [Source.CategoryId]
        }, ActorId, default);
        var draftRevision = saved.GetProperty("draftRevision").GetString()!;
        var preview = await harness.Operations.Preview(new(draftRevision), ActorId, default);
        Assert.True(preview.GetProperty("canPublish").GetBoolean());

        var publicationRevision = preview.GetProperty("publicationRevision").GetString()!;
        var omitted = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Publish(
            new(draftRevision, publicationRevision), ActorId, default));
        var rejected = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Publish(
            new(draftRevision, publicationRevision) { ConfirmedTaxProfile = false }, ActorId, default));

        Assert.Equal("TaxProfileConfirmationRequired", omitted.ErrorCode);
        Assert.Equal("TaxProfileConfirmationRequired", rejected.ErrorCode);
        Assert.Equal(0, harness.MenuProvider.UploadCount);
    }

    [Fact]
    public async Task CategoryDraftCanBeExplicitlyRestoredToFixedModeAfterCapabilityRollback()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);
        var initial = await harness.Operations.CatalogueCategories(default);
        Assert.Equal("categoryItemsV1", initial.GetProperty("selectionMode").GetString());
        Assert.Equal(1_000, initial.GetProperty("maximumCategoryCount").GetInt32());
        Assert.Equal(2_000, initial.GetProperty("maximumItemOverrideCount").GetInt32());

        var categoryDraft = new TenantManagementDraftRequest(initial.GetProperty("draftRevision").GetString(), [])
        {
            ExpectedSourceRevision = initial.GetProperty("sourceRevision").GetString()!,
            CategoryIds = [Source.CategoryId]
        };
        var savedCategory = await harness.Operations.SaveDraft(categoryDraft, ActorId, default);
        var categoryRevision = savedCategory.GetProperty("draftRevision").GetString()!;
        Assert.Equal("categoryItemsV1", savedCategory.GetProperty("selectionMode").GetString());

        harness.Management.CategorySelectionEnabled = false;
        var fixedView = await harness.Operations.Catalogue(default);
        Assert.Equal("fixedItemsV1", fixedView.GetProperty("selectionMode").GetString());
        Assert.Equal(categoryRevision, fixedView.GetProperty("draftRevision").GetString());
        var fixedCategories = await harness.Operations.CatalogueCategories(default);
        Assert.Equal("fixedItemsV1", fixedCategories.GetProperty("selectionMode").GetString());
        Assert.Equal(categoryRevision, fixedCategories.GetProperty("draftRevision").GetString());
        await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Preview(new(categoryRevision), ActorId, default));

        var restored = await harness.Operations.SaveDraft(new(categoryRevision, [new("meal", ProductId, null)]), ActorId, default);
        var restoredRevision = restored.GetProperty("draftRevision").GetString()!;
        var preview = await harness.Operations.Preview(new(restoredRevision), ActorId, default);
        Assert.True(preview.GetProperty("canPublish").GetBoolean());
        var published = await harness.Operations.Publish(new(restoredRevision,
            preview.GetProperty("publicationRevision").GetString()!), ActorId, default);
        Assert.Equal("verified", published.GetProperty("state").GetString());
    }

    [Fact]
    public async Task NullCategoryCollectionsAreRejectedAsClientErrors()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);

        var saveError = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.SaveDraft(
            new(null, []) { ExpectedSourceRevision = new string('b', 64), CategoryIds = null! }, ActorId, default));
        var checkError = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.CheckCategorySelection(
            new(new string('b', 64), null!, [], []), default));

        Assert.Equal(400, saveError.Status);
        Assert.Equal(400, checkError.Status);
    }

    [Fact]
    public async Task CategoryCheckReturnsFreshSupportStatusForRetainedOverride()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);
        var response = await harness.Operations.CheckCategorySelection(new(new string('b', 64),
            [Source.CategoryId], [new(ProductId, null, Source.CategoryId)],
            [new(ProductId, null, Source.CategoryId, false)]), default);

        var status = Assert.Single(response.GetProperty("itemStatuses").EnumerateArray());
        Assert.Equal($"{ProductId:D}:base", status.GetProperty("selectionKey").GetString());
        Assert.Equal(ProductId, status.GetProperty("productId").GetGuid());
        Assert.Equal(Source.CategoryId, status.GetProperty("categoryId").GetGuid());
        Assert.Equal(Source.CategoryId, status.GetProperty("currentCategoryId").GetGuid());
        Assert.False(status.GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task CatalogueKeepsStaleCategoryDraftVisibleWithoutTreatingItAsPublishable()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);
        var initial = await harness.Operations.CatalogueCategories(default);
        var saved = await harness.Operations.SaveDraft(new(
            initial.GetProperty("draftRevision").GetString(), [])
        {
            ExpectedSourceRevision = initial.GetProperty("sourceRevision").GetString()!,
            CategoryIds = [Source.CategoryId]
        }, ActorId, default);
        var draftRevision = saved.GetProperty("draftRevision").GetString();
        var mappingRevision = saved.GetProperty("mappingRevision").GetString();
        var draftSourceRevision = saved.GetProperty("sourceRevision").GetString();

        harness.Catalogue.CurrentRevision = new string('d', 64);
        harness.Catalogue.CategoryName = "Fresh menu";
        var view = await harness.Operations.Catalogue(default);

        Assert.Equal("categoryItemsV1", view.GetProperty("selectionMode").GetString());
        Assert.Equal(draftRevision, view.GetProperty("draftRevision").GetString());
        Assert.Equal(mappingRevision, view.GetProperty("mappingRevision").GetString());
        Assert.Equal(draftSourceRevision, view.GetProperty("draftSourceRevision").GetString());
        Assert.Equal(new string('d', 64), view.GetProperty("sourceRevision").GetString());
        Assert.True(view.GetProperty("sourceChanged").GetBoolean());
        Assert.False(view.GetProperty("canPublish").GetBoolean());
        Assert.Equal("source_changed", view.GetProperty("blockingCodes")[0].GetString());
        Assert.Empty(view.GetProperty("warningCodes").EnumerateArray());
        Assert.Equal("Fresh menu", view.GetProperty("categories")[0].GetProperty("name").GetString());
        Assert.Equal("empty", view.GetProperty("categories")[0].GetProperty("selectionState").GetString());
        Assert.Equal(ProductId, view.GetProperty("selectedItems")[0].GetProperty("productId").GetGuid());
        Assert.True(view.GetProperty("selectedItems")[0].GetProperty("supported").GetBoolean());
        Assert.Equal(1, harness.Catalogue.ReadSelectionCount);

        var previewError = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Preview(
            new(draftRevision!), ActorId, default));
        Assert.Equal(409, previewError.Status);
        Assert.Equal("SourceRevisionChanged", previewError.ErrorCode);
    }

    [Fact]
    public async Task CatalogueRecoversIfSourceChangesBetweenSourceAndPreviewReads()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);
        var initial = await harness.Operations.CatalogueCategories(default);
        var saved = await harness.Operations.SaveDraft(new(
            initial.GetProperty("draftRevision").GetString(), [])
        {
            ExpectedSourceRevision = initial.GetProperty("sourceRevision").GetString()!,
            CategoryIds = [Source.CategoryId]
        }, ActorId, default);
        var draftRevision = saved.GetProperty("draftRevision").GetString();
        var draftSourceRevision = saved.GetProperty("sourceRevision").GetString();
        harness.Catalogue.CategoryName = "Fresh menu";
        harness.Catalogue.ChangeRevisionAfterRead = new string('d', 64);

        var view = await harness.Operations.Catalogue(default);

        Assert.Equal(draftRevision, view.GetProperty("draftRevision").GetString());
        Assert.Equal(draftSourceRevision, view.GetProperty("draftSourceRevision").GetString());
        Assert.Equal(new string('d', 64), view.GetProperty("sourceRevision").GetString());
        Assert.True(view.GetProperty("sourceChanged").GetBoolean());
        Assert.False(view.GetProperty("canPublish").GetBoolean());
        Assert.Equal("source_changed", view.GetProperty("blockingCodes")[0].GetString());
    }

    [Fact]
    public async Task CatalogueDoesNotTreatOtherSourceFailuresAsRecoverableStaleness()
    {
        var harness = CreateHarness(categorySelectionEnabled: true);
        var initial = await harness.Operations.CatalogueCategories(default);
        await harness.Operations.SaveDraft(new(
            initial.GetProperty("draftRevision").GetString(), [])
        {
            ExpectedSourceRevision = initial.GetProperty("sourceRevision").GetString()!,
            CategoryIds = [Source.CategoryId]
        }, ActorId, default);
        harness.Catalogue.ReadError = new ChannelConsoleException(502, "Provider source unavailable.", "SourceUnavailable");

        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Catalogue(default));

        Assert.Equal(502, error.Status);
        Assert.Equal("SourceUnavailable", error.ErrorCode);
    }

    [Fact]
    public async Task SummaryUsesExactProviderStoreNameAndToleratesNameReadFailure()
    {
        var harness = CreateHarness();
        var summary = await harness.Operations.Summary(default);
        Assert.Equal("Sofra Sandbox Kitchen", summary.GetProperty("storeDisplayName").GetString());

        harness.Connection.ConfigurationState = ProviderJson.Encode(new
        {
            enabled = true,
            orderManager = true,
            pending = false,
            manualAcceptance = true,
            storeId = GatewayFixture.StoreId.ToString("D")
        });
        harness.AvailabilityStatus.State = ProviderJson.Encode(new
        {
            items = new[] { new { fresh = true } }
        });
        harness.Connection.FailStoreName = true;
        summary = await harness.Operations.Summary(default);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("storeDisplayName").ValueKind);
        Assert.Equal("connected", summary.GetProperty("connectionStatus").GetString());
        Assert.Equal("healthy", summary.GetProperty("healthStatus").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("degradedReason").ValueKind);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("missing-item")]
    public async Task UnknownProviderMenuDoesNotInventPricesOrCrashDraftReview(string fault)
    {
        var operations = CreateOperations(fault);
        var saved = await operations.SaveDraft(new(null, [new("meal", ProductId, null)]), ActorId, default);
        var catalogue = await operations.Catalogue(default);
        var preview = await operations.Preview(new(saved.GetProperty("draftRevision").GetString()!), ActorId, default);
        foreach (var response in new[] { saved, catalogue, preview })
        {
            var item = response.GetProperty("items")[0];
            Assert.Equal(JsonValueKind.Null, item.GetProperty("providerPriceMinor").ValueKind);
            Assert.Equal("unknown", item.GetProperty("providerPriceStatus").GetString());
            Assert.Equal(725, item.GetProperty("tenantPriceMinor").GetInt32());
        }
        Assert.Equal("reviewedTemplate", preview.GetProperty("serviceHoursStatus").GetString());
        Assert.Equal("unknown", preview.GetProperty("currentServiceHoursStatus").GetString());
    }

    [Fact]
    public async Task ImportExceptionWireHasActualUpdateTimeAndCanBeOpenedByItsIdentity()
    {
        var olderOrder = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var newerOrder = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var created = DateTimeOffset.Parse("2026-10-01T09:00:00Z");
        var updated = DateTimeOffset.Parse("2026-10-02T09:00:00Z");
        var imports = ProviderJson.Encode(new
        {
            items = new[]
        {
            new { orderId = olderOrder, state = "Quarantined", code = "PayloadExpired", createdAt = created, updatedAt = updated },
            new { orderId = newerOrder, state = "Quarantined", code = "ContractRejected", createdAt = created.AddHours(1), updatedAt = updated.AddHours(-1) }
        },
            truncated = false
        });
        var operations = CreateOperations(imports: imports);
        var inbox = await operations.Exceptions("", default);
        var first = inbox.GetProperty("items")[0];
        Assert.Equal(olderOrder, first.GetProperty("id").GetGuid());
        Assert.Equal(created, first.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(updated, first.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.False(first.GetProperty("canReconcile").GetBoolean());
        Assert.False(first.TryGetProperty("OperationId", out _));
        Assert.False(first.TryGetProperty("operationId", out _));
        var detail = await operations.Exception(olderOrder, default);
        Assert.Equal(first.GetRawText(), detail.GetRawText());
    }

    [Fact]
    public async Task AuditFailurePreventsDisconnectPauseAndDraftMutations()
    {
        var harness = CreateHarness();
        harness.Audit.FailWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Operations.Disconnect(GatewayFixture.StoreId, ActorId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Operations.Pause(new(15), ActorId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Operations.SaveDraft(new(null, [new("meal", ProductId, null)]), ActorId, default));

        Assert.Equal(0, harness.Flows.CancelPendingCount);
        Assert.Equal(0, harness.ConnectionState.SetCount);
        Assert.Equal(0, harness.Overrides.SetCount);
        Assert.Equal(0, harness.Connection.EnableOrdersCount);
        Assert.Equal(0, harness.Drafts.SaveCount);
    }

    [Fact]
    public async Task DisconnectUsesTheStoreLeaseAndCancelsPendingAuthorizationAfterIntent()
    {
        var busy = CreateHarness();
        busy.Jobs.LeaseAvailable = false;
        await Assert.ThrowsAsync<ChannelConsoleException>(() => busy.Operations.Disconnect(GatewayFixture.StoreId, ActorId, default));
        Assert.Empty(busy.Audit.Records);
        Assert.Equal(0, busy.Flows.CancelPendingCount);
        Assert.Equal(0, busy.Connection.EnableOrdersCount);

        var ready = CreateHarness();
        var response = await ready.Operations.Disconnect(GatewayFixture.StoreId, ActorId, default);
        Assert.Equal("disconnected", response.GetProperty("status").GetString());
        Assert.Equal(1, ready.Flows.CancelPendingCount);
        Assert.Equal("Intent", ready.Audit.Records[0].ResultCode);
        Assert.Equal("Confirmed", ready.Audit.Records[^1].ResultCode);
    }

    [Fact]
    public async Task PublicationReconciliationHoldsStoreLeaseAcrossReadbackAndStateChange()
    {
        var busy = CreateHarness();
        var busyPublication = await SeedPendingPublication(busy);
        busy.Jobs.LeaseAvailable = false;
        var busyResult = await busy.Operations.Reconcile(busyPublication.Id, ActorId, default);
        Assert.Equal("reconciling", busyResult.GetProperty("status").GetString());
        Assert.Equal("PublicationInProgress", busyResult.GetProperty("code").GetString());
        Assert.Equal(0, busy.MenuProvider.ReadCount);
        Assert.Empty(busy.Audit.Records);

        var harness = CreateHarness();
        var publication = await SeedPendingPublication(harness);
        var readEntered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.MenuProvider.BeforeRead = async menu =>
        {
            readEntered.TrySetResult(menu);
            await releaseRead.Task;
        };
        var reconciliation = harness.Operations.Reconcile(publication.Id, ActorId, default);
        var captured = await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(Menu().GetRawText(), captured.GetRawText());

        await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Publication.Publish(
            Menu(), publication.Revision, harness.Store, default));
        Assert.Equal(0, harness.MenuProvider.UploadCount);
        releaseRead.TrySetResult();

        var result = await reconciliation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("resolved", result.GetProperty("status").GetString());
        Assert.Equal("PreviousMenuConfirmed", result.GetProperty("code").GetString());
        Assert.Equal(CataloguePublicationStates.Abandoned, (await harness.Publications.Find(
            new("sandbox-client", harness.Store.StoreId, harness.Store.TenantId, harness.Store.CatalogueRevision),
            publication.Id, default))?.State);
        Assert.Equal(1, harness.MenuProvider.ReadCount);
    }

    [Fact]
    public async Task ManagerPauseAndResumeFailClosedWhileAvailabilityWriteHoldsStoreLease()
    {
        var harness = CreateHarness();
        var updateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new UberAvailability(new Dictionary<string, bool> { ["meal"] = false })
        {
            BeforeUpdate = async () =>
            {
                updateEntered.TrySetResult();
                await releaseUpdate.Task;
            }
        };
        var policy = ChannelProcessingPolicyTestSupport.Availability(harness.Bridge, harness.Webhook,
            new Source(), new Resolver(harness.Store), harness.Overrides, TimeProvider.System);
        var processor = new ChannelAvailabilityProcessor(provider, harness.Jobs, TimeProvider.System, policy);
        var processing = processor.Process(default);
        await updateEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Pause(new(15), ActorId, default));
        await Assert.ThrowsAsync<ChannelConsoleException>(() => harness.Operations.Resume(ActorId, default));
        Assert.Equal(0, harness.Overrides.SetCount);
        Assert.Empty(harness.Audit.Records);
        releaseUpdate.TrySetResult();

        await processing.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(provider.Items["meal"]);
        Assert.Equal(0, harness.Overrides.SetCount);
    }

    private static async Task<CataloguePublication> SeedPendingPublication(OperationsHarness harness)
    {
        var preview = await harness.Publication.Preview(Menu(), harness.Store, default);
        var menu = preview.GetProperty("menu").Clone();
        return await harness.Publications.Begin(new("sandbox-client", harness.Store.StoreId, harness.Store.TenantId,
                harness.Store.CatalogueRevision), new(CatalogueMenuPlanner.MappingHash(harness.Store),
            ProviderJson.Text(preview, "sourceRevision"), ProviderJson.Text(preview, "revision"), menu, Menu(),
            ProviderJson.Encode(new { catalogueRevision = harness.Store.CatalogueRevision, items = Array.Empty<object>() })), default);
    }

    private static TenantChannelManagementOperations CreateOperations(string readFault = "", JsonElement? imports = null)
        => CreateHarness(readFault, imports).Operations;

    private static OperationsHarness CreateHarness(string readFault = "", JsonElement? imports = null,
        bool categorySelectionEnabled = false, JsonElement? providerMenu = null)
    {
        var store = new TenantStoreBinding
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = "management-tenant",
            BaseUrl = "https://tenant.example/",
            ApiToken = "order-ingress-fixture",
            CatalogueApiToken = "catalogue-fixture",
            Currency = "EUR",
            CatalogueRevision = "deployment-revision",
            PublishedMenuHash = new string('a', 64),
            Items = [new() { ProviderItemId = "meal", ProductId = Guid.Parse("30000000-0000-0000-0000-000000000001") }]
        };
        var drafts = new Drafts(); var publications = new Publications(); var jobs = new Jobs();
        var flows = new OAuthFlows(); var overrides = new Overrides(); var connectionState = new ConnectionState();
        var connection = new Connection(); var audit = new Audit();
        var catalogue = new Source();
        var reviewedMenu = providerMenu ?? Menu();
        var menuProvider = new MenuProvider(reviewedMenu);
        var webhook = Options.Create(new UberWebhookSettings { ClientId = "sandbox-client", StoreIds = [store.StoreId] });
        var bridge = Options.Create(new TenantBridgeSettings
        {
            Enabled = true,
            UseTenantCatalogue = true,
            SyncAvailability = true,
            Store = store
        });
        var managementSettings = new TenantManagementGatewaySettings
        { Enabled = true, CategorySelectionEnabled = categorySelectionEnabled };
        var management = Options.Create(managementSettings);
        var context = new TenantManagementContext(bridge, management, webhook, TimeProvider.System);
        var publication = new TenantCataloguePublication(context, catalogue, publications, jobs,
            menuProvider, new Resolver(store));
        var catalogueState = new TenantCatalogueManagementState(drafts, publications, new Resolver(store), jobs);
        var availabilityState = new TenantChannelAvailabilityState(new Resolver(store), jobs, overrides);
        var availabilityStatus = new AvailabilityStatus(); var tenantAvailability = new Source();
        var menu = new SandboxMenuStub(reviewedMenu, menuProvider) { ReadFault = readFault };
        var summary = new TenantChannelSummaryService(context, connectionState, publications, connection, availabilityStatus);
        var catalogueOperations = new TenantChannelCatalogueService(context, catalogueState, menu, catalogue, publication, audit);
        var availabilityOperations = new TenantChannelAvailabilityService(context, availabilityState, availabilityStatus,
            tenantAvailability, audit);
        var exceptionData = new TenantChannelExceptionData(context, catalogueState, availabilityState,
            new Imports(imports), audit, flows, connectionState);
        var exceptionOperations = new TenantChannelExceptionService(context, exceptionData,
            new TenantChannelPublicationReconciler(context, catalogueState, menu, audit),
            new TenantChannelAvailabilityReconciler(context, availabilityState, tenantAvailability, new UberAvailability(), audit));
        var connectionOperations = new TenantChannelConnectionService(context, connection, flows, connectionState,
            availabilityState, audit);
        var operations = new TenantChannelManagementOperations(summary, catalogueOperations, availabilityOperations,
            exceptionOperations, connectionOperations);

        return new(operations, audit, flows, overrides, connectionState, connection, drafts, jobs, publication, publications,
            menuProvider, store, bridge.Value, webhook.Value, availabilityStatus, managementSettings)
        { Catalogue = catalogue };
    }

    private sealed record OperationsHarness(TenantChannelManagementOperations Operations, Audit Audit,
        OAuthFlows Flows, Overrides Overrides, ConnectionState ConnectionState, Connection Connection, Drafts Drafts, Jobs Jobs,
        TenantCataloguePublication Publication, Publications Publications, MenuProvider MenuProvider, TenantStoreBinding Store,
        TenantBridgeSettings Bridge, UberWebhookSettings Webhook, AvailabilityStatus AvailabilityStatus,
        TenantManagementGatewaySettings Management)
    {
        public Source Catalogue { get; init; } = new();
    }

    private static JsonElement Menu() => JsonDocument.Parse("""
        {"menus":[{"id":"menu","service_availability":[{"day_of_week":"monday","time_periods":[{"start_time":"09:00","end_time":"17:00"}]}]}],
         "categories":[],"items":[{"id":"meal","title":{"translations":{"en_us":"Old name"}},
         "description":{"translations":{"en_us":"Old description"}},"price_info":{"price":500},
         "tax_info":{"vat_rate_percentage":9}}],"modifier_groups":[]}
        """).RootElement.Clone();

    private static JsonElement CategoryMenu() => JsonDocument.Parse("""
        {"menus":[{"id":"menu","category_ids":[],"service_availability":[{"day_of_week":"monday","time_periods":[{"start_time":"09:00","end_time":"17:00"}]}]}],
         "categories":[{"id":"template-category","title":{"translations":{"en_us":"Template category"}},"entities":[]}],
         "items":[{"id":"template-item","title":{"translations":{"en_us":"Template item"}},
         "description":{"translations":{"en_us":"Template description"}},"price_info":{"price":500},
         "tax_info":{"vat_rate_percentage":9},"suspension_info":{"suspend_until":null}}],"modifier_groups":[]}
        """).RootElement.Clone();

    private sealed class Source : ITenantCatalogueClient, ITenantAvailabilityClient
    {
        private static readonly string Fingerprint = new('c', 64);
        public static readonly Guid CategoryId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        public string CurrentRevision { get; set; } = new('b', 64);
        public string CategoryName { get; set; } = "Main menu";
        public string? ChangeRevisionAfterRead { get; set; }
        public ChannelConsoleException? ReadError { get; set; }
        public int ReadSelectionCount { get; private set; }
        public Task<TenantCatalogueCategories> Categories(TenantStoreBinding store, string expectedSourceRevision,
            IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemReference> itemReferences,
            IReadOnlyList<TenantCatalogueItemOverride> overrides, CancellationToken cancellationToken)
            => Task.FromResult(new TenantCatalogueCategories(CurrentRevision, "en",
                [new(CategoryId, CategoryName, 0, 1, 1, 0, true)])
            {
                SourceChanged = expectedSourceRevision.Length > 0 && expectedSourceRevision != CurrentRevision,
                ItemStatuses = itemReferences.Concat(overrides.Select(row => new TenantCatalogueItemReference(
                        row.ProductId, row.VariationId, row.CategoryId)))
                    .GroupBy(row => (row.ProductId, row.VariationId)).Select(group => group.First())
                    .Select(row => new TenantCatalogueItemStatus($"{row.ProductId:D}:{row.VariationId?.ToString("D") ?? "base"}",
                        row.ProductId, row.VariationId, row.CategoryId!.Value, row.CategoryId, false)).ToArray()
            });

        public Task<TenantCatalogueSelection> ReadSelection(TenantStoreBinding store, string expectedSourceRevision,
            IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemOverride> overrides,
            CancellationToken cancellationToken)
        {
            ReadSelectionCount++;
            if (expectedSourceRevision != CurrentRevision)
                throw new ChannelConsoleException(409, "The tenant catalogue changed.", "SourceRevisionChanged");
            var item = new TenantCatalogueSelectionItem($"{ProductId:D}:base", ProductId, null, CategoryId,
                CategoryName, 0, 0, "Sofra meal", "Reviewed description", null, 725, true, true, "", Fingerprint);
            var category = new TenantCatalogueSelectionCategory(CategoryId, CategoryName, 0, 1, 1, 0, 1, 0, true);
            return Task.FromResult(new TenantCatalogueSelection(CurrentRevision, "en", [category], categoryIds,
                [], [item]));
        }

        public Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
        {
            if (ReadError is not null) throw ReadError;
            if (store.SourceRevision.Length > 0 && store.SourceRevision != CurrentRevision)
                throw new ChannelConsoleException(409, "The tenant catalogue changed.", "SourceRevisionChanged");
            var items = store.Items.Select(item => new TenantCatalogueItem(item.ProductId, item.VariationId,
                "Sofra meal", "Reviewed description", item.VariationName, 725, true, "")
            {
                SelectionKey = item.SelectionKey,
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryName,
                CategoryDisplayOrder = item.CategoryDisplayOrder,
                ItemDisplayOrder = item.ItemDisplayOrder,
                SourceFingerprint = item.SourceFingerprint
            }).ToArray();
            var categories = store.Categories.Select(row => new TenantCatalogueSelectionCategory(row.CategoryId,
                row.Name, row.DisplayOrder, row.TotalItemCount, row.SupportedItemCount, row.UnsupportedItemCount,
                row.SelectedItemCount, row.SelectedUnsupportedItemCount, row.Active)).ToArray();
            var snapshot = new TenantCatalogueSnapshot(CurrentRevision, items)
            { Language = store.Language, Categories = categories };
            if (ChangeRevisionAfterRead is { } revision)
            {
                CurrentRevision = revision;
                ChangeRevisionAfterRead = null;
            }
            return Task.FromResult(snapshot);
        }
        Task<TenantAvailabilitySnapshot> ITenantAvailabilityClient.Read(TenantStoreBinding store, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAvailabilitySnapshot(CurrentRevision, store.Items.Select(item =>
                new TenantAvailabilityItem(item.ProviderItemId, true, "Available")).ToArray()));
    }

    private sealed class MenuProvider(JsonElement menu) : ITenantMenuProvider
    {
        private JsonElement _menu = menu;
        public int ReadCount { get; private set; }
        public int UploadCount { get; private set; }
        public Func<JsonElement, Task>? BeforeRead { get; set; }
        public async Task<JsonElement> Read(CancellationToken cancellationToken)
        {
            var snapshot = _menu.Clone(); ReadCount++;
            if (BeforeRead is not null) await BeforeRead(snapshot);
            return snapshot;
        }
        public Task Upload(JsonElement value, CancellationToken cancellationToken)
        { UploadCount++; _menu = value.Clone(); return Task.CompletedTask; }
    }

    private sealed class SandboxMenuStub(JsonElement menu, MenuProvider provider) : ISandboxMenu
    {
        public JsonElement Preview() => menu.Clone();
        public string ReadFault { get; init; } = string.Empty;
        public Task<JsonElement> Read(CancellationToken cancellationToken) => ReadFault switch
        {
            "unavailable" => Task.FromException<JsonElement>(new ChannelConsoleException(502, "Provider unavailable")),
            "missing-item" => Task.FromResult(JsonDocument.Parse("{\"menus\":[],\"items\":[]}").RootElement.Clone()),
            _ => provider.Read(cancellationToken)
        };
        public Task<JsonElement> Publish(CancellationToken cancellationToken) => Task.FromResult(menu.Clone());
        public Task<JsonElement> Publish(string revision, CancellationToken cancellationToken) => Publish(cancellationToken);
        public Task RequireVerified(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement> Expected(CancellationToken cancellationToken) => Task.FromResult(menu.Clone());
    }

    private sealed class Drafts : ICatalogueMappingDrafts
    {
        private CatalogueMappingDraft? _draft;
        public int SaveCount { get; private set; }
        public Task<CatalogueMappingDraft?> Read(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult(_draft);
        public Task<bool> Save(AvailabilityBinding binding, CatalogueMappingDraft draft, string? expectedRevision, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (_draft?.Revision != expectedRevision) return Task.FromResult(false);
            _draft = draft; return Task.FromResult(true);
        }
    }

    private sealed class Publications : ICataloguePublications
    {
        private CataloguePublication? _latest;
        public Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult(_latest);
        public Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_latest?.Id == id ? _latest : null);
        public Task<CataloguePublication> Begin(AvailabilityBinding binding, CataloguePublicationIntent intent,
            CancellationToken cancellationToken)
            => Task.FromResult(_latest = new(Guid.NewGuid(), intent.MappingHash, intent.SourceRevision, intent.Revision,
                intent.Menu, intent.PreviousMenu, CataloguePublicationStates.Pending, null, null, intent.MappingSnapshot));
        public Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (_latest?.Id != id) return Task.FromResult(false);
            _latest = _latest with { State = CataloguePublicationStates.Verified, ProviderHash = providerHash, VerifiedAt = now };
            return Task.FromResult(true);
        }
        public Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
        {
            if (_latest?.Id != id) return Task.FromResult(false);
            _latest = _latest with { State = CataloguePublicationStates.Abandoned }; return Task.FromResult(true);
        }
    }

    private sealed class Resolver(TenantStoreBinding store) : ICatalogueMappingResolver
    {
        public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => Task.FromResult(store);
        public Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken) => Task.FromResult(store);
    }

    private sealed class Jobs : IChannelAvailabilityJobs
    {
        private int _held;
        public bool LeaseAvailable { get; set; } = true;
        public Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
        {
            if (!LeaseAvailable || Interlocked.CompareExchange(ref _held, 1, 0) != 0)
                return Task.FromResult<IChannelAvailabilityLease?>(null);
            return Task.FromResult<IChannelAvailabilityLease?>(new Lease(() => Interlocked.Exchange(ref _held, 0)));
        }
        public Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChannelAvailabilityState>>([]);
    }

    private sealed class Lease(Action release) : IChannelAvailabilityLease
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) release();
            return ValueTask.CompletedTask;
        }
        public Task<bool> Queue(string sourceRevision, IReadOnlyList<ChannelAvailabilityDesired> items, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> Observe(string itemId, string sourceRevision, string state, bool? observedAvailable, string? providerHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Audit : IChannelManagementAudit
    {
        public bool FailWrites { get; set; }
        public List<(string Action, string ResultCode)> Records { get; } = [];
        public Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode, Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            if (FailWrites) throw new InvalidOperationException("Audit store unavailable.");
            Records.Add((action, resultCode)); return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding, long? beforeSequence, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChannelManagementAuditRecord>>([]);
    }

    private sealed class OAuthFlows : ITenantOAuthFlows
    {
        public int CancelPendingCount { get; private set; }
        public Task Create(TenantOAuthFlow flow, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TenantOAuthFlow?> Read(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task<TenantOAuthFlow?> FindByStateHash(AvailabilityBinding binding, string stateHash, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task Expire(AvailabilityBinding binding, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TenantOAuthFlow?> Claim(AvailabilityBinding binding, string stateHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<TenantOAuthFlow?>(null);
        public Task<bool> FailPending(AvailabilityBinding binding, Guid id, string stateHash, string errorCode, DateTimeOffset completedAt, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> CancelPending(AvailabilityBinding binding, string errorCode, DateTimeOffset now, CancellationToken cancellationToken)
        { CancelPendingCount++; return Task.FromResult(1); }
        public Task<bool> Finish(AvailabilityBinding binding, Guid id, string status, string? errorCode, DateTimeOffset completedAt, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Overrides : IChannelAvailabilityOverrides
    {
        public int SetCount { get; private set; }
        public Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken) => Task.FromResult<ChannelAvailabilityOverride?>(null);
        public Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused, DateTimeOffset? pausedUntil, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
        { SetCount++; return Task.FromResult(new ChannelAvailabilityOverride(isPaused, pausedUntil, actorId, now)); }
    }

    private sealed class ConnectionState : IChannelManagementConnectionState
    {
        public int SetCount { get; private set; }
        public Task<ChannelManagementConnectionState> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelManagementConnectionState(false, null, null));
        public Task<ChannelManagementConnectionState> Set(AvailabilityBinding binding, bool isDisconnected, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
        { SetCount++; return Task.FromResult(new ChannelManagementConnectionState(isDisconnected, actorId, now)); }
    }

    private sealed class AvailabilityStatus : IChannelAvailabilityStatus
    {
        public JsonElement State { get; set; } = ProviderJson.Encode(new { enabled = true, paused = false, items = Array.Empty<object>() });
        public Task<JsonElement> Read(CancellationToken cancellationToken) => Task.FromResult(State);
    }

    private sealed class Imports(JsonElement? body = null) : IChannelImportView
    {
        public Task<JsonElement> Read(string cursor, CancellationToken cancellationToken) => Task.FromResult(body ?? ProviderJson.Encode(new { items = Array.Empty<object>(), truncated = false }));
    }

    private sealed class UberAvailability : IUberAvailabilityClient
    {
        public UberAvailability(Dictionary<string, bool>? items = null) => Items = items ?? new(StringComparer.Ordinal);
        public Dictionary<string, bool> Items { get; }
        public Func<Task>? BeforeUpdate { get; init; }
        public Task<UberAvailabilitySnapshot> Read(TenantStoreBinding store, string clientId, CancellationToken cancellationToken)
            => Task.FromResult(new UberAvailabilitySnapshot("hash", new Dictionary<string, bool>(Items, StringComparer.Ordinal)));
        public async Task Update(TenantStoreBinding store, TenantAvailabilityItem item, CancellationToken cancellationToken)
        {
            if (BeforeUpdate is not null) await BeforeUpdate();
            Items[item.ProviderItemId] = item.Available;
        }
    }

    private sealed class Connection : ISandboxConnection
    {
        public int EnableOrdersCount { get; private set; }
        public bool FailStoreName { get; set; }
        public JsonElement ConfigurationState { get; set; } = ProviderJson.Encode(new { });
        public Task<string> Start(string sessionHash, CancellationToken cancellationToken, bool enableTesting = false) => Task.FromResult(string.Empty);
        public Task Complete(string sessionHash, string state, string code, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement> ConnectTenant(string merchantToken, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> ConnectTenant(string merchantToken, bool enableOrderAcceptance, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> Configuration(CancellationToken cancellationToken) => Task.FromResult(ConfigurationState);
        public Task<string?> StoreDisplayName(CancellationToken cancellationToken) => FailStoreName
            ? Task.FromException<string?>(new ChannelConsoleException(502, "Provider store details are unavailable."))
            : Task.FromResult<string?>("Sofra Sandbox Kitchen");
        public Task<JsonElement> EnableOrders(bool enable, CancellationToken cancellationToken)
        { EnableOrdersCount++; return Task.FromResult(ProviderJson.Encode(new { })); }
    }
}
