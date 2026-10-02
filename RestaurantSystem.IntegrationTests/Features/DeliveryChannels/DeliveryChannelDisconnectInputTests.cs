using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class DeliveryChannelDisconnectInputTests(DatabaseFixture fixture) : ApiTokenScopeTestBase(fixture)
{
    private readonly Mock<IDeliveryChannelManagementClient> _gateway = new(MockBehavior.Strict);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<DeliveryChannelSettings>(settings =>
        {
            settings.Enabled = true;
            settings.SandboxOnly = true;
            settings.Stores = [new() { Provider = "uber-eats", StoreId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", Currency = "CHF", IsSandbox = true }];
        });
        services.PostConfigure<DeliveryChannelManagementSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = "isolated-fixture-tenant";
            settings.GatewayBaseUrl = "https://gateway.example/";
            settings.ServerCredential = new string('a', 64);
        });
        services.AddSingleton(_gateway.Object);
    }

    [Fact]
    public async Task MissingStoreIsRejectedBeforeTheDisconnectCanReachTheGateway()
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync("/api/delivery-channels/management/uber/disconnect", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExplicitStoreReachesTheDisconnectAction()
    {
        var storeId = Guid.NewGuid();
        _gateway.Setup(gateway => gateway.Send<DeliveryChannelDisconnectResultDto>(HttpMethod.Post,
                "api/tenant-management/uber/disconnect", It.IsAny<Guid>(),
                It.Is<object>(body => ((DeliveryChannelDisconnectRequest)body).StoreId == storeId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryChannelDisconnectResultDto("disconnected", true, true, null, DateTimeOffset.UtcNow));
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync("/api/delivery-channels/management/uber/disconnect", new { storeId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _gateway.VerifyAll();
    }
}
