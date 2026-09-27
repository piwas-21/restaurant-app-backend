using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 4")]
public sealed class CatalogueCategoryTranslationProvenanceTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITranslationGenerationProvider>();
        services.AddSingleton<ITranslationGenerationProvider, CountingProvider>();
        services.Configure<TranslationAssistanceSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantDataApproved = true;
            settings.ApiUrl = TranslationProviderTestSettings.Endpoint;
            settings.ApiKey = "test-only-key"; // pragma: allowlist secret -- inert test value
        });
    }

    [Fact]
    public async Task ImportAcceptsOnlyReviewedTemplateTextAndUnchangedCategoryNeedsNoGeneration()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<ICatalogueTemplateImportExecutor>();
        var stateStore = scope.ServiceProvider.GetRequiredService<ICatalogueImportStateStore>();
        var reviewedRevision = Revision("category-reviewed", "reviewed");
        var draftRevision = Revision("category-draft", "draft");
        var reviewedTemplate = Template(reviewedRevision);
        var draftTemplate = Template(draftRevision);
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            AdoptionId = Guid.NewGuid(),
            RootTemplateId = reviewedRevision.TemplateId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreateNewCopy = true,
            CreatedBy = "integration-test",
            Templates = [reviewedTemplate, draftTemplate]
        };
        var batch = await stateStore.LoadBatchContextAsync(session, CancellationToken.None);

        var reviewed = await executor.ExecuteAsync(session, reviewedTemplate, batch, CancellationToken.None);
        reviewed.Status.Should().Be(CatalogueImportItemStatus.Imported);
        reviewed.LocalEntityId.Should().NotBeNull();
        var draftImport = async () => await executor.ExecuteAsync(
            session, draftTemplate, batch, CancellationToken.None);
        await draftImport.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Only reviewed catalogue template revisions can be imported.");
        var evidence = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => row.EntityType == "category" && row.EntityId == reviewed.LocalEntityId!.Value)
            .ToListAsync();
        evidence.Should().HaveCount(4);
        evidence.Should().OnlyContain(row => row.Kind == "template" &&
            row.TemplateId == reviewedRevision.TemplateId && row.TemplateRevision == 1);
        evidence.Select(row => (row.FieldKey, row.Locale)).Should().BeEquivalentTo(new[]
        {
            ("name", "en"), ("name", "fr"), ("description", "en"), ("description", "fr")
        });
        (await context.Categories.AsNoTracking().AnyAsync(row => row.Name == draftRevision.Name))
            .Should().BeFalse();

        var provider = Factory.Services.GetRequiredService<ITranslationGenerationProvider>()
            .Should().BeOfType<CountingProvider>().Subject;
        var suggestions = await PostAsJsonAsync("/api/translation-workbench/suggestions", new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "en", "fr" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "category", entityId = reviewed.LocalEntityId, fieldKey = "name" },
                sourceLocale = "en",
                sourceText = reviewedRevision.Name
            } }
        });
        suggestions.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var response = JsonDocument.Parse(await suggestions.Content.ReadAsStringAsync());
        response.RootElement.GetProperty("data").GetProperty("suggestions").GetArrayLength().Should().Be(0);
        provider.Calls.Should().Be(0);
    }

    private static CentralCatalogueTemplateRevision Revision(string templateId, string qualityStatus) => new()
    {
        SchemaVersion = 1,
        TemplateId = templateId,
        Revision = 1,
        Type = "category",
        Name = $"Imported {templateId}",
        Description = "Reviewed category description",
        SourceLocale = "en",
        Translations = new Dictionary<string, CentralCatalogueTranslation>
        {
            ["fr"] = new() { Name = $"Importé {templateId}", Description = "Description de catégorie" }
        },
        Dependencies = [],
        Provenance = JsonSerializer.SerializeToElement(new { source = "fixture" }),
        QualityStatus = qualityStatus,
        CompatibleTenantContractVersions = [1],
        Payload = JsonSerializer.SerializeToElement(new { sortOrder = 0 }),
        ContentHash = new string('a', 64)
    };

    private static CatalogueImportSessionTemplate Template(CentralCatalogueTemplateRevision revision) => new()
    {
        Id = Guid.NewGuid(),
        TemplateId = revision.TemplateId,
        Revision = revision.Revision,
        Type = revision.Type,
        ContentHash = revision.ContentHash,
        RevisionJson = JsonSerializer.Serialize(revision, JsonOptions),
        DecisionJson = JsonSerializer.Serialize(new CatalogueImportItemDecision { Resolution = "Create" }, JsonOptions),
        IsSelected = true,
        Status = CatalogueImportItemStatus.Pending,
        CreatedBy = "integration-test"
    };

    private sealed class CountingProvider : ITranslationGenerationProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<TranslationGenerationResult> GenerateAsync(
            IReadOnlyList<TranslationGenerationTarget> targets,
            IReadOnlyDictionary<string, string> glossary,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new TranslationGenerationResult(
                targets.ToDictionary(target => target.Key, _ => "generated"),
                "test", "fake", 100, 10));
        }
    }
}
