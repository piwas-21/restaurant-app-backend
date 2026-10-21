using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.TableGuestVisits;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundIngressTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<TableGuestVisitSettings>(settings =>
        {
            settings.RoundAttemptsPerMinute = 10;
            settings.RoundAttemptsPerIpPerMinute = 3;
        });
    }

    [Fact]
    public async Task Equivalent_native_routes_share_the_legacy_round_ip_budget_even_with_rotating_tokens()
    {
        var intent = await RoundIntentAsync();
        foreach (var action in new[] { "prepare", "submit", "recover" })
        {
            using var response = await SendUntrustedAsync(RoundPath + action, intent);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        }
        using var blocked = await SendUntrustedAsync(RoundPath + "cancel-attempt", intent);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var legacy = await SendUntrustedAsync($"/api/table-guest-visits/{Visit.ServiceSessionId}/rounds", intent);
        legacy.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var health = await Client.GetAsync("/api/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public void Every_native_action_uses_the_existing_round_policy()
    {
        var actions = typeof(CustomerMobileTableRoundsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        actions.Select(value => value.Name).Should().BeEquivalentTo("Prepare", "Submit", "Recover", "Stop");
        foreach (var action in actions)
        {
            var policy = action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
            policy.Should().Be(TableGuestVisitRateLimitPolicies.RoundPolicyName);
        }
    }

    private async Task<HttpResponseMessage> SendUntrustedAsync<T>(string path, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Session-Id", Session);
        request.Headers.Add("X-Table-Participant", TableGuestCredentialCrypto.CreateParticipantToken());
        return await Client.SendAsync(request);
    }
}
