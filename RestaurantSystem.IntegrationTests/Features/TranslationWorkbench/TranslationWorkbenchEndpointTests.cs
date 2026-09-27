using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

[Collection("Database Lane 2")]
public sealed class TranslationWorkbenchEndpointTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CompleteOrLegacyTextDoesNotGenerateAndOnlyMissingTargetsAreReported()
    {
        AuthenticateAsAdmin();
        var request = new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "tr", "en", "fr" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "product", clientKey = "new-product", fieldKey = "name" }, // pragma: allowlist secret -- draft identity
                sourceLocale = "tr",
                sourceText = "Tavuk",
                targetTexts = new Dictionary<string, string>
                {
                    ["tr"] = "Tavuk", ["en"] = "Chicken", ["fr"] = ""
                }
            } }
        };

        var preview = await PostAsJsonAsync("/api/translation-workbench/preview", request);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var targets = previewJson.RootElement.GetProperty("data").GetProperty("rows")[0]
            .GetProperty("targets");
        targets[1].GetProperty("status").GetString().Should().Be("current");
        targets[1].GetProperty("provenance").GetProperty("kind").GetString()
            .Should().Be("legacyUnknown");
        targets[2].GetProperty("status").GetString().Should().Be("missing");

        var suggestions = await PostAsJsonAsync("/api/translation-workbench/suggestions", request);
        suggestions.StatusCode.Should().Be(HttpStatusCode.OK);
        using var suggestionJson = JsonDocument.Parse(await suggestions.Content.ReadAsStringAsync());
        var data = suggestionJson.RootElement.GetProperty("data");
        data.GetProperty("suggestions").GetArrayLength().Should().Be(0);
        data.GetProperty("skipped").GetArrayLength().Should().Be(1);
        data.GetProperty("skipped")[0].GetProperty("locale").GetString().Should().Be("fr");
        data.GetProperty("providerStatus").GetString().Should().Be("disabled");
    }

    [Fact]
    public async Task PersistedSourceLocaleAppearsOnAdminProductRead()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var now = DateTime.UtcNow;
        context.TranslationFieldProvenances.Add(new TranslationFieldProvenance
        {
            Id = Guid.NewGuid(),
            EntityType = "product",
            EntityId = productId,
            FieldKey = "name",
            Locale = "tr",
            SourceLocale = "tr",
            SourceHash = TranslationWorkbenchRules.Hash("tr\nTavuk"),
            TextHash = TranslationWorkbenchRules.Hash("Tavuk"),
            Kind = "tenantSource",
            ReviewStatus = "source",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "test",
            UpdatedBy = "test"
        });
        await context.SaveChangesAsync();

        var response = await Client.GetAsync($"/api/Products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetProperty("translationMetadata")
            .GetProperty("sourceLocales").GetProperty("name").GetString().Should().Be("tr");
    }

    [Fact]
    public async Task SourceEditMarksOnlyTrackedSuggestionStaleAndKeepsManualText()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ITranslationProvenanceWriter>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var oldHash = TranslationWorkbenchRules.Hash("tr\nTavuk");
        var now = DateTime.UtcNow;
        context.TranslationFieldProvenances.AddRange(
            Evidence("en", "Chicken", "manual"), Evidence("fr", "Poulet", "ai"));
        var suggestion = new TranslationSuggestion
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            EntityType = "product",
            EntityId = productId,
            FieldKey = "name",
            Locale = "de",
            SourceLocale = "tr",
            SourceHash = oldHash,
            ContextHash = new string('0', 64),
            Fingerprint = new string('1', 64),
            SuggestedText = "Hähnchen",
            Provider = "test",
            Model = "test",
            Status = "suggested",
            RequestedBy = "test",
            CreatedAt = now,
            CreatedBy = "test"
        };
        context.TranslationSuggestions.Add(suggestion);
        await context.SaveChangesAsync();

        await writer.RecordAsync("product", productId,
            new TranslationOwnerMetadataDto
            {
                SourceLocales = new Dictionary<string, string> { ["name"] = "tr" }
            }, TranslationTextMap.Create("Yeni tavuk", null,
                [("tr", "Yeni tavuk", null), ("en", "Chicken", null), ("fr", "Poulet", null)]),
            CancellationToken.None);
        await context.SaveChangesAsync();

        var evidence = await context.TranslationFieldProvenances
            .Where(row => row.EntityId == productId).ToListAsync();
        evidence.Single(row => row.Locale == "en").Kind.Should().Be("manual");
        evidence.Single(row => row.Locale == "fr").SourceHash.Should().Be(oldHash);
        (await context.TranslationSuggestions.SingleAsync(row => row.Id == suggestion.Id))
            .Status.Should().Be("stale");

        var preview = await PostAsJsonAsync("/api/translation-workbench/preview", new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "tr", "en", "fr" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "product", entityId = productId, fieldKey = "name" },
                sourceLocale = "tr", sourceText = "Yeni tavuk",
                targetTexts = new Dictionary<string, string>
                {
                    ["tr"] = "Yeni tavuk", ["en"] = "Chicken", ["fr"] = "Poulet"
                }
            } }
        });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var targets = json.RootElement.GetProperty("data").GetProperty("rows")[0]
            .GetProperty("targets");
        targets[1].GetProperty("status").GetString().Should().Be("current");
        targets[2].GetProperty("status").GetString().Should().Be("stale");

        TranslationFieldProvenance Evidence(string locale, string text, string kind) => new()
        {
            Id = Guid.NewGuid(),
            EntityType = "product",
            EntityId = productId,
            FieldKey = "name",
            Locale = locale,
            SourceLocale = "tr",
            SourceHash = oldHash,
            TextHash = TranslationWorkbenchRules.Hash(text),
            Kind = kind,
            ReviewStatus = "reviewed",
            CreatedAt = now,
            CreatedBy = "test"
        };
    }

    [Fact]
    public async Task TemplateProvenanceRecordsOnlyReviewedTextThatMatchesSavedValues()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ITranslationProvenanceWriter>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var matched = await writer.RecordTemplateAsync("product", productId,
            "turkish-chicken", 2, "tr",
            TranslationTextMap.Create("Tavuk", null,
                [("tr", "Tavuk", null), ("en", "Chicken", null), ("fr", "Poulet maison", null)]),
            [new("name", "tr", "Tavuk"), new("name", "en", "Chicken"),
                new("name", "fr", "Poulet")], CancellationToken.None);
        await context.SaveChangesAsync();

        matched.Should().Be(2);
        var rows = await context.TranslationFieldProvenances
            .Where(row => row.EntityId == productId).ToListAsync();
        rows.Select(row => row.Locale).Should().BeEquivalentTo("tr", "en");
        rows.Should().OnlyContain(row => row.Kind == "template" &&
            row.TemplateId == "turkish-chicken" && row.TemplateRevision == 2);
    }

    [Fact]
    public async Task AcceptedSuggestionMustMatchTheExactSavedSourceAndTarget()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ITranslationProvenanceWriter>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var suggestion = new TranslationSuggestion
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            EntityType = "product",
            EntityId = productId,
            FieldKey = "name",
            Locale = "en",
            SourceLocale = "tr",
            SourceHash = TranslationWorkbenchRules.Hash("tr\nTavuk"),
            ContextHash = new string('0', 64),
            Fingerprint = new string('1', 64),
            SuggestedText = "Chicken",
            ReviewedText = "Chicken",
            Provider = "test",
            Model = "test",
            Status = "accepted",
            RequestedBy = "test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.TranslationSuggestions.Add(suggestion);
        await context.SaveChangesAsync();
        var metadata = new TranslationOwnerMetadataDto
        {
            SourceLocales = new Dictionary<string, string> { ["name"] = "tr" },
            AcceptedSuggestionIds = new Dictionary<string, string>
            {
                ["name.en"] = suggestion.Id.ToString()
            }
        };
        await writer.RecordAsync("product", productId, metadata,
            TranslationTextMap.Create("Tavuk", null, [("en", "Chicken", null)]),
            CancellationToken.None);
        await context.SaveChangesAsync();
        (await context.TranslationFieldProvenances.SingleAsync(row =>
            row.EntityId == productId && row.Locale == "en")).Kind.Should().Be("ai");

        var staleSave = () => writer.RecordAsync("product", productId, metadata,
            TranslationTextMap.Create("Yeni tavuk", null, [("en", "Chicken", null)]),
            CancellationToken.None);
        await staleSave.Should().ThrowAsync<ConflictException>();
    }
}
