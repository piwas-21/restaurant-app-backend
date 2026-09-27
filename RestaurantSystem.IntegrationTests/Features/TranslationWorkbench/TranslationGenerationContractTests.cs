using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

public sealed class TranslationGenerationContractTests
{
    [Fact]
    public void AcceptedTranslationRequiresTheTextVersionLoadedBeforeAnotherSave()
    {
        var product = new Product
        {
            Name = "Tavuk",
            CreatedBy = "test",
            Descriptions =
            [
                new ProductDescription { Lang = "en", Name = "Chicken", Description = "", CreatedBy = "test" },
                new ProductDescription { Lang = "fr", Name = "Poulet", Description = "", CreatedBy = "test" }
            ]
        };
        var loadedVersion = TranslationContentVersion.ForProduct(product);
        TranslationContentVersion.EnsureCurrent(product, loadedVersion, 1);

        product.Descriptions.Single(row => row.Lang == "fr").Name = "Poulet grillé";
        var staleSave = () => TranslationContentVersion.EnsureCurrent(product, loadedVersion, 1);
        staleSave.Should().Throw<ConflictException>()
            .WithMessage("Menu text changed. Reload before saving reviewed translations.");
        TranslationContentVersion.EnsureCurrent(product, null, 0);
    }

    [Theory]
    [InlineData("Tacos 2 {count} €5", "2 tacos {count} €5", true)]
    [InlineData("Tacos 2 {count} €5", "2 tacos {count} €6", false)]
    [InlineData("Tacos 2 {count} €5", "2 tacos {missing} €5", false)]
    [InlineData("Tacos 2 {count} €5", "2 tacos €5", false)]
    public void GeneratedTextMustPreserveNumbersCurrencyAndPlaceholders(
        string source, string proposed, bool expected)
    {
        TranslationWorkbenchRules.IsSafeSuggestion(source, proposed, "name").Should().Be(expected);
    }

    [Fact]
    public async Task ProviderSendsConstrainedBatchWithoutStoringIt()
    {
        string? posted = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            posted = await request.Content!.ReadAsStringAsync();
            var response = new
            {
                status = "completed",
                output = new[] { new
                {
                    type = "message",
                    content = new[] { new { type = "output_text", text = "{\"items\":[{\"key\":\"0\",\"text\":\"Chicken\"}] }" } }
                } },
                usage = new { input_tokens = 47, output_tokens = 12 }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            };
        }));
        var provider = new OpenAiTranslationGenerationProvider(client,
            Options.Create(new TranslationAssistanceSettings
            {
                ApiUrl = "https://api.openai.com/v1/responses",
                ApiKey = "test-key" // pragma: allowlist secret -- inert test value
            }));
        var result = await provider.GenerateAsync(
            [new TranslationGenerationTarget("0", "tr", "en", "name", "Tavuk", "Tavuk dürüm", null, [])],
            new Dictionary<string, string> { ["dürüm"] = "wrap" }, CancellationToken.None);

        result.Texts["0"].Should().Be("Chicken");
        result.InputTokens.Should().Be(47);
        using var request = JsonDocument.Parse(posted!);
        request.RootElement.GetProperty("model").GetString().Should().Be("gpt-6-luna");
        request.RootElement.GetProperty("store").GetBoolean().Should().BeFalse();
        request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be("none");
        request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ProviderRejectsUnsafeOutputBeforeItCanBecomeASuggestion()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"items\\\":[{\\\"key\\\":\\\"0\\\",\\\"text\\\":\\\"Chicken €6\\\"}]}\"}]}]}")
            })));
        var provider = new OpenAiTranslationGenerationProvider(client,
            Options.Create(new TranslationAssistanceSettings
            {
                ApiUrl = "https://api.openai.com/v1/responses",
                ApiKey = "test-key" // pragma: allowlist secret -- inert test value
            }));

        var act = () => provider.GenerateAsync(
            [new TranslationGenerationTarget("0", "tr", "en", "name", "Tavuk €5", null, null, [])],
            new Dictionary<string, string>(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ProviderRejectsMissingUsageSoDailyLimitsCannotUndercount()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"items\\\":[{\\\"key\\\":\\\"0\\\",\\\"text\\\":\\\"Chicken\\\"}]}\"}]}]}")
            })));
        var provider = new OpenAiTranslationGenerationProvider(client,
            Options.Create(new TranslationAssistanceSettings
            {
                ApiUrl = "https://api.openai.com/v1/responses",
                ApiKey = "test-key" // pragma: allowlist secret -- inert test value
            }));

        var act = () => provider.GenerateAsync(
            [new TranslationGenerationTarget("0", "tr", "en", "name", "Tavuk", null, null, [])],
            new Dictionary<string, string>(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Translation provider returned invalid usage");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
