using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

public sealed class GeminiTranslationGenerationProviderTests
{
    [Fact]
    public async Task SendsStructuredLowThinkingRequestAndCountsThinkingTokens()
    {
        string? posted = null;
        Uri? endpoint = null;
        string? apiKey = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            posted = await request.Content!.ReadAsStringAsync();
            endpoint = request.RequestUri;
            apiKey = request.Headers.GetValues("x-goog-api-key").Single();
            return JsonResponse(HttpStatusCode.OK, new
            {
                candidates = new[]
                {
                    new
                    {
                        finishReason = "STOP",
                        content = new { role = "model", parts = new[]
                        {
                            new { text = "{\"items\":[{\"key\":\"0\",\"text\":\"Chicken €5 {count}\"}]}" }
                        } }
                    }
                },
                usageMetadata = new
                {
                    promptTokenCount = 80,
                    candidatesTokenCount = 10,
                    thoughtsTokenCount = 4,
                    totalTokenCount = 94
                }
            });
        }));
        var settings = ConfiguredSettings();
        settings.HasProviderConfiguration.Should().BeTrue();
        var provider = new GeminiTranslationGenerationProvider(client, Options.Create(settings));

        var result = await provider.GenerateAsync(
            [Target()], new Dictionary<string, string> { ["dürüm"] = "wrap" }, CancellationToken.None);

        endpoint!.AbsolutePath.Should().Be("/v1beta/models/gemini-3.8-flash:generateContent");
        apiKey.Should().Be("inert-gemini-test-key");
        result.Texts["0"].Should().Be("Chicken €5 {count}");
        result.Provider.Should().Be("gemini");
        result.Model.Should().Be("gemini-3.8-flash");
        result.InputTokens.Should().Be(80);
        result.OutputTokens.Should().Be(14, "Gemini bills both thought tokens and visible response tokens");

        using var request = JsonDocument.Parse(posted!);
        var root = request.RootElement;
        root.GetProperty("generationConfig").GetProperty("thinkingConfig")
            .GetProperty("thinkingLevel").GetString().Should().Be("low");
        root.GetProperty("generationConfig").GetProperty("responseFormat")
            .GetProperty("text").GetProperty("mimeType").GetString().Should().Be("application/json");
        root.GetProperty("generationConfig").GetProperty("responseFormat")
            .GetProperty("text").GetProperty("schema").GetProperty("required")[0]
            .GetString().Should().Be("items");
        root.GetProperty("systemInstruction").GetProperty("parts")[0]
            .GetProperty("text").GetString().Should().Contain("Preserve every number");
        root.GetProperty("generationConfig").TryGetProperty("temperature", out _).Should().BeFalse();
        root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text")
            .GetString().Should().Contain("Tavuk");
    }

    [Fact]
    public async Task RejectsProviderFailureWithoutSurfacingResponseBody()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("provider error body must not escape")
            })));
        var provider = new GeminiTranslationGenerationProvider(client, Options.Create(ConfiguredSettings()));

        var act = () => provider.GenerateAsync([Target()], new Dictionary<string, string>(), CancellationToken.None);

        var error = await act.Should().ThrowAsync<HttpRequestException>();
        error.Which.Message.Should().Be("Translation provider rejected the request");
        error.Which.Message.Should().NotContain("provider error body");
    }

    [Fact]
    public async Task RejectsUsageWhenThinkingCostCannotBeCounted()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, new
        {
            candidates = new[]
            {
                new
                {
                    finishReason = "STOP",
                    content = new { parts = new[] { new { text = "{\"items\":[{\"key\":\"0\",\"text\":\"Chicken\"}]}" } } }
                }
            },
            usageMetadata = new { promptTokenCount = 80, candidatesTokenCount = 10 }
        }))));
        var provider = new GeminiTranslationGenerationProvider(client, Options.Create(ConfiguredSettings()));

        var act = () => provider.GenerateAsync([Target()], new Dictionary<string, string>(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Translation provider returned invalid usage");
    }

    [Fact]
    public void DefaultConfigurationStaysDisabledAndDoesNotContainAGeminiCredential()
    {
        var settings = new TranslationAssistanceSettings();

        settings.Enabled.Should().BeFalse();
        settings.TenantDataApproved.Should().BeFalse();
        settings.Provider.Should().Be("openai");
        settings.Gemini.Model.Should().Be("gemini-3.8-flash");
        settings.Gemini.ApiKey.Should().BeEmpty();
        settings.HasProviderConfiguration.Should().BeFalse();
        settings.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void GeminiReadinessRequiresProviderConfigurationAndBothEnablementGates()
    {
        var settings = ConfiguredSettings();

        settings.CanGenerate.Should().BeFalse();
        settings.Enabled = true;
        settings.CanGenerate.Should().BeFalse();
        settings.TenantDataApproved = true;
        settings.CanGenerate.Should().BeTrue();
        settings.Gemini.Model = "bad/model";
        settings.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void GeminiCanBeSelectedByConfigurationWithoutEnablingGeneration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TranslationAssistance:Provider"] = "gemini"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTranslationWorkbench(configuration);
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<TranslationAssistanceSettings>>().Value;

        provider.GetRequiredService<ITranslationGenerationProvider>()
            .Should().BeOfType<GeminiTranslationGenerationProvider>();
        settings.Enabled.Should().BeFalse();
        settings.TenantDataApproved.Should().BeFalse();
        settings.HasProviderConfiguration.Should().BeFalse();
        settings.CanGenerate.Should().BeFalse();
    }

    private static TranslationAssistanceSettings ConfiguredSettings() => new()
    {
        Provider = "gemini",
        MaxOutputTokens = 256,
        TimeoutSeconds = 3,
        Gemini = new GeminiTranslationProviderSettings
        {
            ApiBaseUrl = TranslationProviderTestSettings.GeminiBaseUrl,
            ApiKey = "inert-gemini-test-key", // pragma: allowlist secret -- inert test value
            Model = "gemini-3.8-flash",
            InputCostPerMillionUsd = 1m,
            OutputCostPerMillionUsd = 2m
        }
    };

    private static TranslationGenerationTarget Target() =>
        new("0", "tr", "en", "name", "Tavuk €5 {count}", "Tavuk dürüm", null, []);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
