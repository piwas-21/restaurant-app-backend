using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.TableGuestVisits;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class TableGuestVisitRateLimitPolicyTests
{
    [Fact]
    public void Guest_visit_rate_defaults_and_bounds_are_validated()
    {
        var settings = new TableGuestVisitSettings();
        settings.AccountReadsPerMinute.Should().Be(60);
        settings.RoundAttemptsPerMinute.Should().Be(10);
        settings.AccountReadsPerIpPerMinute.Should().Be(600);
        settings.RoundAttemptsPerIpPerMinute.Should().Be(100);
        settings.IsValid().Should().BeTrue();

        settings.AccountReadsPerMinute = 0;
        settings.IsValid().Should().BeFalse();
        settings.AccountReadsPerMinute = 601;
        settings.IsValid().Should().BeFalse();
        settings.AccountReadsPerMinute = 60;
        settings.RoundAttemptsPerMinute = 0;
        settings.IsValid().Should().BeFalse();
        settings.RoundAttemptsPerMinute = 61;
        settings.IsValid().Should().BeFalse();
        settings.RoundAttemptsPerMinute = 10;
        settings.AccountReadsPerIpPerMinute = 0;
        settings.IsValid().Should().BeFalse();
        settings.AccountReadsPerIpPerMinute = 6001;
        settings.IsValid().Should().BeFalse();
        settings.AccountReadsPerIpPerMinute = 600;
        settings.RoundAttemptsPerIpPerMinute = 0;
        settings.IsValid().Should().BeFalse();
        settings.RoundAttemptsPerIpPerMinute = 1001;
        settings.IsValid().Should().BeFalse();
    }

    [Fact]
    public void Account_and_round_routes_use_their_separate_credential_buckets()
    {
        PolicyFor(nameof(TableGuestVisitsController.GetAccount))
            .Should().Be(TableGuestVisitRateLimitPolicies.AccountPolicyName);
        PolicyFor(nameof(TableGuestVisitsController.CreateRound))
            .Should().Be(TableGuestVisitRateLimitPolicies.RoundPolicyName);
        TableGuestVisitRateLimitPolicies.AccountPolicyName
            .Should().NotBe(TableGuestVisitRateLimitPolicies.RoundPolicyName);
    }

    [Fact]
    public void Valid_credentials_partition_by_digest_and_invalid_credentials_only_by_ip()
    {
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var digest).Should().BeTrue();
        var first = Context(token, IPAddress.Parse("192.0.2.10"));
        var second = Context(TableGuestCredentialCrypto.CreateParticipantToken(), IPAddress.Parse("192.0.2.10"));
        var invalid = Context("not-a-participant-secret", IPAddress.Parse("192.0.2.10"));

        var firstKey = TableGuestVisitRateLimitPolicies.ResolvePartitionKey(first);
        firstKey.Should().Be($"participant:{digest}");
        firstKey.Should().NotContain(token);
        TableGuestVisitRateLimitPolicies.ResolvePartitionKey(second).Should().NotBe(firstKey);
        TableGuestVisitRateLimitPolicies.ResolvePartitionKey(invalid).Should().Be("ip:192.0.2.10");
    }

    [Fact]
    public void Registration_helper_returns_the_configured_rate_limiter_options()
    {
        var options = new RateLimiterOptions();

        TableGuestVisitRateLimitPolicies.AddTableGuestVisitCredentialPolicies(options)
            .Should().BeSameAs(options);
    }

    private static string? PolicyFor(string actionName) => typeof(TableGuestVisitsController)
        .GetMethod(actionName)!
        .GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

    private static DefaultHttpContext Context(string token, IPAddress address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        context.Request.Headers["X-Table-Participant"] = token;
        return context;
    }
}

[Collection("Database Lane 3")]
public sealed class TableGuestVisitRateLimitEndpointTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private TableGuestVisitSettings _effectiveSettings = null!;

    public TableGuestVisitRateLimitEndpointTests(DatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_fixture.ConnectionString, new Dictionary<string, string>
        {
            ["TableGuestVisits:AccountReadsPerMinute"] = "1",
            ["TableGuestVisits:RoundAttemptsPerMinute"] = "1",
            ["TableGuestVisits:AccountReadsPerIpPerMinute"] = "3",
            ["TableGuestVisits:RoundAttemptsPerIpPerMinute"] = "3",
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        _effectiveSettings = _factory.Services.GetRequiredService<IOptions<TableGuestVisitSettings>>().Value;
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Limiter_uses_host_overrides_and_separate_participant_digest_buckets()
    {
        _effectiveSettings.AccountReadsPerMinute.Should().Be(1);
        _effectiveSettings.RoundAttemptsPerMinute.Should().Be(1);
        var sessionId = Guid.NewGuid();
        var firstToken = TableGuestCredentialCrypto.CreateParticipantToken();
        var otherToken = TableGuestCredentialCrypto.CreateParticipantToken();

        (await ReadAccount(sessionId, firstToken)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ReadAccount(sessionId, otherToken)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ReadAccount(sessionId, firstToken)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var operationId = Guid.NewGuid();
        (await CreateRound(sessionId, operationId, firstToken)).StatusCode
            .Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CreateRound(sessionId, operationId, otherToken)).StatusCode
            .Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CreateRound(sessionId, operationId, firstToken)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Random_canonical_tokens_cannot_bypass_the_shared_ip_budget()
    {
        _effectiveSettings.AccountReadsPerIpPerMinute.Should().Be(3);
        _effectiveSettings.RoundAttemptsPerIpPerMinute.Should().Be(3);
        var sessionId = Guid.NewGuid();
        for (var index = 0; index < 3; index++)
        {
            using var read = await ReadAccount(sessionId, TableGuestCredentialCrypto.CreateParticipantToken());
            read.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
        using var blockedRead = await ReadAccount(sessionId, TableGuestCredentialCrypto.CreateParticipantToken());
        blockedRead.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Reads and writes retain separate budgets even behind the same restaurant Wi-Fi.
        for (var index = 0; index < 3; index++)
        {
            using var round = await CreateRound(sessionId, Guid.NewGuid(),
                TableGuestCredentialCrypto.CreateParticipantToken());
            round.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
        using var blockedRound = await CreateRound(sessionId, Guid.NewGuid(),
            TableGuestCredentialCrypto.CreateParticipantToken());
        blockedRound.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var unrelated = await _client.GetAsync("/api/health");
        unrelated.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpResponseMessage> ReadAccount(Guid sessionId, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/table-guest-visits/{sessionId}/account");
        request.Headers.Add("X-Table-Participant", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CreateRound(Guid sessionId, Guid operationId, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/table-guest-visits/{sessionId}/rounds")
        {
            Content = JsonContent.Create(new
            {
                operationId,
                expectedAccountRevision = 1,
                expectedBasketFingerprint = new string('A', 64),
            }),
        };
        request.Headers.Add("X-Table-Participant", token);
        request.Headers.Add("X-Session-Id", "test-basket-capability");
        return await _client.SendAsync(request);
    }
}
