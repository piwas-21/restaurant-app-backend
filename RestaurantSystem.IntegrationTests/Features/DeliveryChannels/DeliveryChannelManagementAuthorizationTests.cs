using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class DeliveryChannelManagementAuthorizationTests(DatabaseFixture fixture) : ApiTokenScopeTestBase(fixture)
{
    private const string Route = "/api/delivery-channels/management/uber";
    private const string Store = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Tenant = "isolated-fixture-tenant";
    private readonly GatewayHandler _gateway = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<DeliveryChannelSettings>(settings =>
        {
            settings.Enabled = true; settings.SandboxOnly = true;
            settings.Stores = [new() { Provider = "uber-eats", StoreId = Store, Currency = "CHF", IsSandbox = true }];
        });
        services.PostConfigure<DeliveryChannelManagementSettings>(settings =>
        {
            settings.Enabled = true; settings.TenantId = Tenant;
            settings.GatewayBaseUrl = "https://gateway.example/";
            settings.ServerCredential = new string('a', 64);
        });
        services.AddHttpClient<IDeliveryChannelManagementClient, DeliveryChannelManagementClient>()
            .ConfigurePrimaryHttpMessageHandler(() => _gateway);
    }

    [Theory]
    [InlineData(UserRole.Customer)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task NonAdminsCannotReadOrMutateTenantManagement(UserRole role)
    {
        AuthenticateAsRole(role);
        (await Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.GetAsync($"{Route}/catalogue/candidates")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsJsonAsync($"{Route}/oauth/start", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _gateway.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnonymousAndMachineCredentialsCannotBecomeHumanManagement()
    {
        AuthenticateAsAnonymous();
        (await Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelOrdersWrite, ApiTokenScopes.ChannelCatalogueRead]));
        (await Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsJsonAsync($"{Route}/availability/resume", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _gateway.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminProxyUsesDeploymentBindingIgnoresHostileHeadersAndNeverReturnsCredential()
    {
        AuthenticateAsAdmin();
        Client.DefaultRequestHeaders.Add("X-Sofra-Tenant-Id", "another-tenant");
        Client.DefaultRequestHeaders.Add("X-Uber-Store-Id", Guid.NewGuid().ToString());
        Client.DefaultRequestHeaders.Add("X-Sofra-Actor-Id", Guid.NewGuid().ToString());
        var response = await Client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var sent = _gateway.Calls.Should().ContainSingle().Subject;
        sent.Tenant.Should().Be(Tenant); sent.Store.Should().Be(Store);
        sent.Actor.Should().Be(TestAuthHandler.AdminUserId);
        sent.Authorization.Should().Be($"Bearer {new string('a', 64)}");
        sent.Uri.Should().Be("https://gateway.example/api/tenant-management/uber/summary");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(new string('a', 64));
        Factory.Services.GetRequiredService<IOptions<DeliveryChannelManagementSettings>>().Value.Enabled = false;
        var disabled = await Client.GetAsync(Route);
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);
        disabled.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var summary = JsonDocument.Parse(await disabled.Content.ReadAsStringAsync());
        summary.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
        summary.RootElement.GetProperty("degradedReason").GetString().Should().Be("IntegrationNotProvisioned");
        summary.RootElement.GetProperty("storeId").GetGuid().Should().Be(Guid.Empty);
        summary.RootElement.GetProperty("currency").GetString().Should().BeEmpty();
        summary.RootElement.GetProperty("requireManualAcceptance").GetBoolean().Should().BeTrue();
        summary.RootElement.GetProperty("capabilities").EnumerateObject()
            .Should().OnlyContain(property => !property.Value.GetBoolean());
        Factory.Services.GetRequiredService<IOptions<DeliveryChannelManagementSettings>>().Value.Enabled = true;
        Factory.Services.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value.Enabled = false;
        using var channelDisabled = await Client.GetAsync(Route);
        channelDisabled.StatusCode.Should().Be(HttpStatusCode.OK);
        _gateway.Calls.Should().HaveCount(1);
        (await Client.GetAsync($"{Route}/availability")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        _gateway.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveDraftRetainsPutAndBoundRevisionWithoutSendingBrowserHeaders()
    {
        AuthenticateAsAdmin();
        var response = await Client.PutAsJsonAsync($"{Route}/catalogue/draft", new
        {
            expectedDraftRevision = "reviewed-draft",
            items = new[]
            { new { providerItemId = "approved-item", productId = Guid.NewGuid(), variationId = (Guid?)null } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _gateway.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Put);
        using var payload = JsonDocument.Parse(call.Body);
        payload.RootElement.GetProperty("expectedDraftRevision").GetString().Should().Be("reviewed-draft");
        call.Tenant.Should().Be(Tenant);
    }

    [Fact]
    public async Task CategoryDraftProxyBindsAndForwardsExplicitEmptyItemsArray()
    {
        AuthenticateAsAdmin();
        var categoryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var productId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var sourceRevision = new string('d', 64);

        var response = await Client.PutAsJsonAsync($"{Route}/catalogue/draft", new
        {
            expectedDraftRevision = "reviewed-draft",
            items = Array.Empty<object>(),
            expectedSourceRevision = sourceRevision,
            categoryIds = new[] { categoryId },
            itemOverrides = new[]
            {
                new { productId, variationId = (Guid?)null, categoryId = (Guid?)categoryId, selected = false }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _gateway.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Put);
        call.Uri.Should().Be("https://gateway.example/api/tenant-management/uber/catalogue/draft");
        using var payload = JsonDocument.Parse(call.Body);
        var json = payload.RootElement;
        json.GetProperty("expectedDraftRevision").GetString().Should().Be("reviewed-draft");
        json.GetProperty("expectedSourceRevision").GetString().Should().Be(sourceRevision);
        json.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
        json.GetProperty("categoryIds")[0].GetGuid().Should().Be(categoryId);
        json.GetProperty("itemOverrides")[0].GetProperty("productId").GetGuid().Should().Be(productId);
        json.GetProperty("itemOverrides")[0].GetProperty("variationId").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("itemOverrides")[0].GetProperty("selected").GetBoolean().Should().BeFalse();
    }

    private sealed record Call(HttpMethod Method, string Uri, string Tenant, string Store,
        string Actor, string Authorization, string Body);

    private sealed class GatewayHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new(request.Method, request.RequestUri!.AbsoluteUri,
                request.Headers.GetValues("X-Sofra-Tenant-Id").Single(), request.Headers.GetValues("X-Uber-Store-Id").Single(),
                request.Headers.GetValues("X-Sofra-Actor-Id").Single(), request.Headers.Authorization!.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"provider\":\"uber-eats\",\"enabled\":true}", Encoding.UTF8, "application/json") };
        }
    }
}
