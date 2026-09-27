using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueImportReviewedListTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-reviewed-list-test";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("omitted", true)]
    [InlineData("null", true)]
    [InlineData("empty", false)]
    public async Task Item_preview_requires_explicit_reviewed_ingredient_and_allergen_lists(string listState, bool blocked)
    {
        var session = await SeedSessionAsync("item");
        AuthenticateAsAdmin();

        using var preview = await SaveAndPreviewAsync(session, listState, listState);
        var blockers = ReadBlockerCodes(preview);

        if (blocked)
        {
            blockers.Should().Contain("TENANT_INGREDIENTS_REQUIRED");
            blockers.Should().Contain("TENANT_ALLERGENS_REQUIRED");
        }
        else
        {
            blockers.Should().NotContain("TENANT_INGREDIENTS_REQUIRED");
            blockers.Should().NotContain("TENANT_ALLERGENS_REQUIRED");
        }
    }

    [Theory]
    [InlineData("null", true)]
    [InlineData("empty", false)]
    public async Task Bundle_preview_requires_an_explicit_reviewed_allergen_list_but_not_unmapped_ingredients(
        string allergenState,
        bool blocked)
    {
        var session = await SeedSessionAsync("bundle");
        AuthenticateAsAdmin();

        using var preview = await SaveAndPreviewAsync(session, "omitted", allergenState);
        var blockers = ReadBlockerCodes(preview);

        blockers.Should().NotContain("TENANT_INGREDIENTS_REQUIRED");
        if (blocked)
        {
            blockers.Should().Contain("TENANT_ALLERGENS_REQUIRED");
        }
        else
        {
            blockers.Should().NotContain("TENANT_ALLERGENS_REQUIRED");
        }
    }

    [Fact]
    public async Task Import_with_a_missing_reviewed_list_writes_no_product()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await SeedBlockedItemSessionAsync(context);
        var productCount = await context.Products.CountAsync();
        var categoryCount = await context.Categories.CountAsync();
        var revisions = session.Templates.ToDictionary(template => template.TemplateId,
            template => JsonSerializer.Deserialize<CentralCatalogueTemplateRevision>(template.RevisionJson, WebOptions)!,
            StringComparer.Ordinal);
        var previewService = scope.ServiceProvider.GetRequiredService<ICatalogueImportPreviewService>();
        var preview = await previewService.PreviewAsync(session.Id, CancellationToken.None);
        preview.Items.Where(item => item.IsSelected).SelectMany(item => item.BlockingIssues)
            .Select(issue => issue.Code).Should().Equal("TENANT_INGREDIENTS_REQUIRED");
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        central.Setup(client => client.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CatalogueCurrentRevisionRequest> requests, CancellationToken _) =>
                new CatalogueProxyResponse(StatusCodes.Status200OK, JsonSerializer.SerializeToElement(new
                {
                    items = requests.Select(request => new
                    {
                        templateId = request.TemplateId,
                        status = "available",
                        revision = revisions[request.TemplateId],
                        adoptedRevisionWithdrawn = false
                    })
                }, WebOptions)));
        var importLock = new Mock<ICatalogueImportLock>();
        importLock.Setup(value => value.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopLease());
        var importer = new CatalogueSessionImporter(
            context,
            importLock.Object,
            new CatalogueImportStateStore(context, scope.ServiceProvider.GetRequiredService<ICurrentUserService>()),
            scope.ServiceProvider.GetRequiredService<ICatalogueTemplateImportExecutor>(),
            previewService,
            central.Object,
            scope.ServiceProvider.GetRequiredService<ILogger<CatalogueSessionImporter>>());

        var act = () => importer.ImportAsync(session.Id,
            new ImportCatalogueSessionRequest { ExpectedVersion = 1, IdempotencyKey = Guid.NewGuid().ToString("N") },
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Resolve the blocking import review issues before importing.");
        exception.Which.ErrorCode.Should().BeNull();
        (await context.Products.CountAsync()).Should().Be(productCount);
        (await context.Categories.CountAsync()).Should().Be(categoryCount);
        (await context.CatalogueImportSessions.AsNoTracking().SingleAsync(value => value.Id == session.Id))
            .Status.Should().Be(CatalogueImportStatus.Draft);
    }

    private static async Task<CatalogueImportSession> SeedBlockedItemSessionAsync(ApplicationDbContext context)
    {
        var categoryId = $"review-category-{Guid.NewGuid():N}";
        var itemId = $"review-item-{Guid.NewGuid():N}";
        var categoryRevision = CreateRevision(categoryId, "category", new { sortOrder = 0 });
        var itemRevision = CreateRevision(itemId, "item", new
        {
            category = new { templateId = categoryId, revision = 1 },
            suggestedIngredients = Array.Empty<object>(),
            optionSets = Array.Empty<object>(),
            sideSets = Array.Empty<object>()
        });
        var category = CreateTemplate(categoryRevision,
            new CatalogueImportItemDecision { Resolution = "Create" }, isRoot: false);
        var item = CreateTemplate(itemRevision, new CatalogueImportItemDecision
        {
            Resolution = "Create",
            LocalPrice = 9.5m,
            LocalProductType = "MainItem",
            IntendedIsAvailable = false,
            Ingredients = null,
            Allergens = [],
            IngredientsReviewed = true,
            AllergensReviewed = true,
            AvailabilityReviewed = true,
            ChannelsReviewed = true,
            KitchenRoutingReviewed = true,
            AvailableOrderTypes = 3,
            KitchenType = KitchenType.BackKitchen
        }, isRoot: true);
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = itemId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = Guid.NewGuid(),
            CreateNewCopy = true,
            Version = 1,
            CreatedBy = Actor,
            Templates = [category, item]
        };
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    private async Task<JsonDocument> SaveAndPreviewAsync(
        CatalogueImportSession session,
        string ingredientState,
        string allergenState)
    {
        var template = session.Templates.Single();
        var decision = CreateDecisionPayload(template.TemplateId, template.Type, ingredientState, allergenState);
        var body = new
        {
            expectedVersion = session.Version,
            selectedTemplateIds = new[] { template.TemplateId },
            decisions = new[] { decision }
        };
        var path = $"/api/catalogue/import-sessions/{session.Id}";
        using var save = await Client.PutAsJsonAsync($"{path}/items", body, WebOptions);
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        using var previewResponse = await Client.PostAsJsonAsync($"{path}/preview", new { }, WebOptions);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
    }

    private async Task<CatalogueImportSession> SeedSessionAsync(
        string type,
        ApplicationDbContext? existingContext = null)
    {
        if (existingContext is not null)
        {
            return await AddSessionAsync(existingContext, type);
        }

        using var scope = Factory.Services.CreateScope();
        return await AddSessionAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), type);
    }

    private static async Task<CatalogueImportSession> AddSessionAsync(ApplicationDbContext context, string type)
    {
        var templateId = $"reviewed-list-{Guid.NewGuid():N}";
        object payload = type == "item"
            ? new
            {
                category = (object?)null,
                suggestedIngredients = Array.Empty<object>(),
                optionSets = Array.Empty<object>(),
                sideSets = Array.Empty<object>()
            }
            : new { sections = Array.Empty<object>() };
        var revision = new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = templateId,
            Revision = 1,
            Type = type,
            Name = $"Reviewed {type}",
            SourceLocale = "en",
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            Payload = JsonSerializer.SerializeToElement(payload),
            ContentHash = new string('d', 64)
        };
        var template = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            Revision = revision.Revision,
            Type = type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, WebOptions),
            IsRoot = true,
            IsSelectable = true,
            IsSelected = true,
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = Actor
        };
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = templateId,
            RootRevision = revision.Revision,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = Guid.NewGuid(),
            CreateNewCopy = true,
            Version = 1,
            CreatedBy = Actor,
            Templates = [template]
        };
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    private static Dictionary<string, object?> CreateDecisionPayload(
        string templateId,
        string type,
        string ingredientState,
        string allergenState)
    {
        var decision = new Dictionary<string, object?>
        {
            ["templateId"] = templateId,
            ["revision"] = 1,
            ["resolution"] = "Create",
            ["localPrice"] = 9.5m,
            ["intendedIsAvailable"] = false,
            ["availabilityReviewed"] = true,
            ["channelsReviewed"] = true,
            ["kitchenRoutingReviewed"] = true,
            ["ingredientsReviewed"] = true,
            ["allergensReviewed"] = true,
            ["availableOrderTypes"] = 3
        };
        if (type == "item")
        {
            decision["localProductType"] = "MainItem";
            decision["kitchenType"] = "BackKitchen";
            decision["choiceRulesReviewed"] = true;
        }

        AddListPayload(decision, "ingredients", ingredientState);
        AddListPayload(decision, "allergens", allergenState);
        return decision;
    }

    private static CentralCatalogueTemplateRevision CreateRevision(string templateId, string type, object payload) => new()
    {
        SchemaVersion = 1,
        TemplateId = templateId,
        Revision = 1,
        Type = type,
        Name = $"Reviewed {type}",
        SourceLocale = "en",
        QualityStatus = "reviewed",
        CompatibleTenantContractVersions = [1],
        Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
        Payload = JsonSerializer.SerializeToElement(payload),
        ContentHash = new string('d', 64)
    };

    private static CatalogueImportSessionTemplate CreateTemplate(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        bool isRoot) => new()
        {
            Id = Guid.NewGuid(),
            TemplateId = revision.TemplateId,
            Revision = revision.Revision,
            Type = revision.Type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, WebOptions),
            DecisionJson = JsonSerializer.Serialize(decision, WebOptions),
            IsRoot = isRoot,
            IsSelectable = true,
            IsSelected = true,
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = Actor
        };

    private static void AddListPayload(Dictionary<string, object?> decision, string field, string state)
    {
        if (state == "omitted") return;
        decision[field] = state == "null" ? null : Array.Empty<string>();
    }

    private static HashSet<string> ReadBlockerCodes(JsonDocument preview)
    {
        var root = preview.RootElement;
        var data = root.TryGetProperty("data", out var wrapped) ? wrapped : root;
        return data.GetProperty("items")[0]
        .GetProperty("blockingIssues")
        .EnumerateArray()
        .Select(issue => issue.GetProperty("code").GetString()!)
        .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
