using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

[Collection("Database Lane 3")]
public sealed class TranslationSuggestionDeduplicationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITranslationGenerationProvider>();
        services.AddSingleton<ITranslationGenerationProvider, CountingProvider>();
        services.Configure<TranslationAssistanceSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantDataApproved = true;
            settings.ApiUrl = "https://translation-test.invalid/v1/responses";
            settings.ApiKey = "test-only-key"; // pragma: allowlist secret -- inert test value
        });
    }

    [Fact]
    public async Task ACompleteFormMakesNoCallAndConcurrentIdenticalGapsShareOneBatch()
    {
        AuthenticateAsAdmin();
        var provider = Factory.Services.GetRequiredService<ITranslationGenerationProvider>()
            .Should().BeOfType<CountingProvider>().Subject;
        var complete = Request("Chicken");
        var completeResponse = await PostAsJsonAsync("/api/translation-workbench/suggestions", complete);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.Calls.Should().Be(0);

        var gap = Request(string.Empty);
        var responses = await Task.WhenAll(
            PostAsJsonAsync("/api/translation-workbench/suggestions", gap),
            PostAsJsonAsync("/api/translation-workbench/suggestions", gap));
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        provider.Calls.Should().Be(1);
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("data").GetProperty("suggestions")
                .GetArrayLength().Should().Be(1);
        }
    }

    [Fact]
    public async Task ExistingLegacyItemCanFillAMissingLocaleOnce()
    {
        AuthenticateAsAdmin();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var productId = await context.Products.Select(product => product.Id).FirstAsync();
        var provider = Factory.Services.GetRequiredService<ITranslationGenerationProvider>()
            .Should().BeOfType<CountingProvider>().Subject;
        var request = new
        {
            generationIntent = "saveReview",
            targetLocales = new[] { "tr", "en" },
            fields = new[] { new
            {
                fieldRef = new { entityType = "product", entityId = productId, fieldKey = "name" },
                sourceLocale = "tr", sourceText = "Tavuk",
                targetTexts = new Dictionary<string, string> { ["tr"] = "Tavuk", ["en"] = "" }
            } }
        };
        var before = provider.Calls;
        var first = await PostAsJsonAsync("/api/translation-workbench/suggestions", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.Calls.Should().Be(before + 1);
        var second = await PostAsJsonAsync("/api/translation-workbench/suggestions", request);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.Calls.Should().Be(before + 1);
    }

    private static object Request(string english) => new
    {
        generationIntent = "saveReview",
        targetLocales = new[] { "tr", "en" },
        fields = new[] { new
        {
            fieldRef = new { entityType = "product", clientKey = "new-product", fieldKey = "name" }, // pragma: allowlist secret -- draft identity
            sourceLocale = "tr", sourceText = "Tavuk",
            targetTexts = new Dictionary<string, string> { ["tr"] = "Tavuk", ["en"] = english }
        } }
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
                targets.ToDictionary(target => target.Key, _ => "Chicken"),
                "test", "fake", 100, 10));
        }
    }
}
