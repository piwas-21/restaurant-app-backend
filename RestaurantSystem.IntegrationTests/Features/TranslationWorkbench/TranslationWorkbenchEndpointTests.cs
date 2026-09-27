using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task ReviewedGeminiTextStaysCurrentUnderGeminiAndIsStaleUnderAnotherProvider()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var geminiSettings = new TranslationAssistanceSettings { Provider = "gemini" };
        var sourceHash = TranslationWorkbenchRules.Hash("tr\nTavuk");
        context.TranslationFieldProvenances.Add(new TranslationFieldProvenance
        {
            Id = Guid.NewGuid(),
            EntityType = "product",
            EntityId = productId,
            FieldKey = "name",
            Locale = "en",
            SourceLocale = "tr",
            SourceHash = sourceHash,
            ContextHash = TranslationWorkbenchRules.ContextHash(null, geminiSettings.Glossary,
                geminiSettings.PromptVersion, geminiSettings.ContextModelKey),
            TextHash = TranslationWorkbenchRules.Hash("Chicken"),
            Kind = "ai",
            ReviewStatus = "reviewed",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        var request = new TranslationWorkbenchRequestDto("saveReview", ["tr", "en"],
            [new TranslationFieldInputDto(
                new TranslationFieldRefDto("product", productId, null, "name"),
                "tr", "Tavuk", new Dictionary<string, string>
                {
                    ["tr"] = "Tavuk", ["en"] = "Chicken"
                })]);
        var reader = new TranslationTextReader(context);
        var geminiPreview = new TranslationPreviewService(context, reader, Options.Create(geminiSettings));
        var openAiPreview = new TranslationPreviewService(context, reader,
            Options.Create(new TranslationAssistanceSettings()));

        var current = await geminiPreview.PreviewAsync(request, CancellationToken.None);
        var stale = await openAiPreview.PreviewAsync(request, CancellationToken.None);

        current.Rows[0].Targets[1].Status.Should().Be("current");
        stale.Rows[0].Targets[1].Status.Should().Be("stale");
    }

    [Fact]
    public async Task OptionSetSavePersistsSourceAndMakesExistingNamesReadableForReview()
    {
        AuthenticateAsAdmin();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceName = $"Acı sos {suffix}";
        var response = await PostAsJsonAsync("/api/OptionSets", new
        {
            kind = 1,
            name = $"Hot sauce {suffix}",
            sourceLocale = "tr",
            translations = new Dictionary<string, string>
            {
                ["tr"] = sourceName,
                ["en"] = $"Hot sauce {suffix}"
            },
            status = 0,
            entries = Array.Empty<object>(),
            translationMetadata = new
            {
                sourceLocales = new Dictionary<string, string> { ["name"] = "tr" }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = saved.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var source = await context.TranslationFieldProvenances.SingleAsync(row =>
            row.EntityType == "optionSet" && row.EntityId == id && row.Locale == "tr");
        source.Kind.Should().Be("tenantSource");
        source.SourceHash.Should().Be(TranslationWorkbenchRules.Hash($"tr\n{sourceName}"));

        var preview = await PostAsJsonAsync("/api/translation-workbench/preview", new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "tr", "en" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "optionSet", entityId = id, fieldKey = "name" },
                sourceLocale = "tr", sourceText = sourceName
            } }
        });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reviewed = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        reviewed.RootElement.GetProperty("data").GetProperty("rows")[0]
            .GetProperty("targets")[1].GetProperty("status").GetString().Should().Be("current");
    }

    [Fact]
    public async Task OptionSetRejectsConflictingSourceMetadata()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/OptionSets", new
        {
            kind = 1,
            name = $"Acı sos {Guid.NewGuid():N}",
            sourceLocale = "tr",
            translations = new Dictionary<string, string> { ["tr"] = "Acı sos" },
            status = 0,
            entries = Array.Empty<object>(),
            translationMetadata = new
            {
                sourceLocales = new Dictionary<string, string> { ["name"] = "en" }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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
    public async Task ExplicitAlternativeCanTargetExistingManualTextWithoutReplacingIt()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/translation-workbench/suggestions", new
        {
            generationIntent = "explicitAlternative",
            targetLocales = new[] { "en" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "product", clientKey = "draft-alternative", fieldKey = "name" }, // pragma: allowlist secret -- draft identity
                sourceLocale = "tr", sourceText = "Tavuk",
                targetTexts = new Dictionary<string, string> { ["en"] = "Chicken" }
            } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("suggestions").GetArrayLength().Should().Be(0);
        data.GetProperty("skipped").GetArrayLength().Should().Be(1);
        data.GetProperty("skipped")[0].GetProperty("locale").GetString().Should().Be("en");
        data.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("providerDisabled");
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
        var metadata = json.RootElement.GetProperty("data").GetProperty("translationMetadata");
        metadata.GetProperty("sourceLocales").GetProperty("name").GetString().Should().Be("tr");
        metadata.GetProperty("expectedContentVersion").GetString().Should().HaveLength(64);
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
    public async Task ChangedGlossaryContextMakesTrackedAiTextStaleButNotManualText()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var sourceHash = TranslationWorkbenchRules.Hash("tr\nTavuk");
        context.TranslationFieldProvenances.AddRange(
            new TranslationFieldProvenance
            {
                Id = Guid.NewGuid(),
                EntityType = "product",
                EntityId = productId,
                FieldKey = "name",
                Locale = "en",
                SourceLocale = "tr",
                SourceHash = sourceHash,
                ContextHash = new string('0', 64),
                TextHash = TranslationWorkbenchRules.Hash("Chicken"),
                Kind = "ai",
                ReviewStatus = "reviewed",
                CreatedBy = "test"
            },
            new TranslationFieldProvenance
            {
                Id = Guid.NewGuid(),
                EntityType = "product",
                EntityId = productId,
                FieldKey = "name",
                Locale = "fr",
                SourceLocale = "tr",
                SourceHash = sourceHash,
                ContextHash = new string('0', 64),
                TextHash = TranslationWorkbenchRules.Hash("Poulet"),
                Kind = "manual",
                ReviewStatus = "reviewed",
                CreatedBy = "test"
            });
        await context.SaveChangesAsync();

        var response = await PostAsJsonAsync("/api/translation-workbench/preview", new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "en", "fr" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "product", entityId = productId, fieldKey = "name" },
                sourceLocale = "tr", sourceText = "Tavuk",
                targetTexts = new Dictionary<string, string>
                {
                    ["en"] = "Chicken", ["fr"] = "Poulet"
                }
            } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var targets = json.RootElement.GetProperty("data").GetProperty("rows")[0]
            .GetProperty("targets");
        targets[0].GetProperty("status").GetString().Should().Be("stale");
        targets[1].GetProperty("status").GetString().Should().Be("current");
    }

    [Fact]
    public async Task TemplateProvenanceRecordsOnlyReviewedTextThatMatchesSavedValues()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ITranslationProvenanceWriter>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var matched = await writer.RecordTemplateAsync(new RecordTemplateTranslationRequest(
            "product", productId, "turkish-chicken", 2, "tr",
            TranslationTextMap.Create("Tavuk", null,
                [("tr", "Tavuk", null), ("en", "Chicken", null), ("fr", "Poulet maison", null)]),
            [new("name", "tr", "Tavuk"), new("name", "en", "Chicken"),
                new("name", "fr", "Poulet")]), CancellationToken.None);
        await context.SaveChangesAsync();

        matched.Should().Be(2);
        var rows = await context.TranslationFieldProvenances
            .Where(row => row.EntityId == productId).ToListAsync();
        rows.Select(row => row.Locale).Should().BeEquivalentTo("tr", "en");
        rows.Should().OnlyContain(row => row.Kind == "template" &&
            row.TemplateId == "turkish-chicken" && row.TemplateRevision == 2);
    }

    [Fact]
    public async Task GlobalIngredientTemplateTextUsesItsOwnIdentityAndSavedTranslations()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ITranslationProvenanceWriter>();
        var ingredient = new GlobalIngredient
        {
            DefaultName = $"Mint {Guid.NewGuid():N}",
            CreatedBy = "test",
            Translations =
            {
                new GlobalIngredientTranslation { LanguageCode = "tr", Name = "Nane", CreatedBy = "test" },
                new GlobalIngredientTranslation { LanguageCode = "en", Name = "Mint", CreatedBy = "test" }
            }
        };
        context.GlobalIngredients.Add(ingredient);
        await context.SaveChangesAsync();

        var matched = await writer.RecordTemplateAsync(new RecordTemplateTranslationRequest(
            "globalIngredient", ingredient.Id, "turkish-mint", 1, "tr",
            TranslationTextMap.Create("Nane", null,
                [("tr", "Nane", null), ("en", "Mint", null)]),
            [new("name", "tr", "Nane"), new("name", "en", "Mint"),
                new("name", "fr", "Menthe")]), CancellationToken.None);
        await context.SaveChangesAsync();
        matched.Should().Be(2);
        var rows = await context.TranslationFieldProvenances
            .Where(row => row.EntityType == "globalIngredient" && row.EntityId == ingredient.Id)
            .ToListAsync();
        rows.Select(row => row.Locale).Should().BeEquivalentTo("tr", "en");

        var preview = await PostAsJsonAsync("/api/translation-workbench/preview", new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "tr", "en", "fr" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "globalIngredient", entityId = ingredient.Id, fieldKey = "name" },
                sourceLocale = "tr", sourceText = "Nane"
            } }
        });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reviewed = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var targets = reviewed.RootElement.GetProperty("data").GetProperty("rows")[0]
            .GetProperty("targets");
        targets[0].GetProperty("status").GetString().Should().Be("current");
        targets[1].GetProperty("status").GetString().Should().Be("current");
        targets[2].GetProperty("status").GetString().Should().Be("missing");
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
        var acceptedEvidence = await context.TranslationFieldProvenances.SingleAsync(row =>
            row.EntityId == productId && row.Locale == "en");
        acceptedEvidence.Kind.Should().Be("ai");
        acceptedEvidence.ContextHash.Should().Be(suggestion.ContextHash);

        var staleSave = () => writer.RecordAsync("product", productId, metadata,
            TranslationTextMap.Create("Yeni tavuk", null, [("en", "Chicken", null)]),
            CancellationToken.None);
        await staleSave.Should().ThrowAsync<ConflictException>();
    }
}
