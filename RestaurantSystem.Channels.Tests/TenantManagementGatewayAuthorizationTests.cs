using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantManagementGatewayAuthorizationTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Theory]
    [InlineData("credential")]
    [InlineData("console-key")]
    [InlineData("tenant")]
    [InlineData("store")]
    [InlineData("actor")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task InvalidOrCrossTenantServerIdentityCannotReachAnyManagementAction(string fault)
    {
        await InScope(async services =>
        {
            var crypto = services.GetRequiredService<ISandboxCrypto>();
            var context = Context();
            if (fault == "credential") context.Request.Headers.Authorization = "Bearer wrong-management-key";
            if (fault == "console-key") context.Request.Headers.Authorization = $"Bearer {AccessKey}";
            if (fault == "tenant") context.Request.Headers["X-Sofra-Tenant-Id"] = "another-tenant";
            if (fault == "store") context.Request.Headers["X-Uber-Store-Id"] = Guid.NewGuid().ToString();
            if (fault == "actor") context.Request.Headers["X-Sofra-Actor-Id"] = Guid.Empty.ToString();
            if (fault == "missing") context.Request.Headers.Remove("X-Sofra-Tenant-Id");
            if (fault == "duplicate") context.Request.Headers.Append("X-Sofra-Tenant-Id", "another-tenant");
            var dispatched = false;
            await new TenantManagementGatewayMiddleware(_ => { dispatched = true; return Task.CompletedTask; })
                .InvokeAsync(context, Management(crypto), Bridge(), Webhook(), crypto);
            Assert.False(dispatched);
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl);
            return true;
        });
    }

    [Fact]
    public async Task ExactServerBindingReachesActionWithActorAndDisabledDeploymentFailsClosed()
    {
        await InScope(async services =>
        {
            var crypto = services.GetRequiredService<ISandboxCrypto>(); var context = Context();
            var middleware = new TenantManagementGatewayMiddleware(next =>
            {
                Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), next.Items[TenantManagementGatewayMiddleware.ActorContextKey]);
                next.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
            await middleware.InvokeAsync(context, Management(crypto), Bridge(), Webhook(), crypto);
            Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
            var disabled = Management(crypto); disabled.Value.Enabled = false;
            var blocked = Context();
            await middleware.InvokeAsync(blocked, disabled, Bridge(), Webhook(), crypto);
            Assert.Equal(StatusCodes.Status404NotFound, blocked.Response.StatusCode);
            return true;
        });
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/tenant-management/uber/catalogue/publish";
        context.Request.Headers.Authorization = $"Bearer {new string('m', 64)}";
        context.Request.Headers["X-Sofra-Tenant-Id"] = "bound-tenant";
        context.Request.Headers["X-Uber-Store-Id"] = GatewayFixture.StoreId.ToString("D");
        context.Request.Headers["X-Sofra-Actor-Id"] = "11111111-1111-1111-1111-111111111111";
        return context;
    }
    private static IOptions<TenantManagementGatewaySettings> Management(ISandboxCrypto crypto)
        => Options.Create(new TenantManagementGatewaySettings { Enabled = true, CredentialHash = crypto.Hash(new string('m', 64)) });
    private static IOptions<TenantBridgeSettings> Bridge()
        => Options.Create(new TenantBridgeSettings { Enabled = true, Store = new() { StoreId = GatewayFixture.StoreId, TenantId = "bound-tenant" } });
    private static IOptions<UberWebhookSettings> Webhook()
        => Options.Create(new UberWebhookSettings
        {
            ClientId = GatewayFixture.ClientId,
            ClientSecret = GatewayFixture.SigningKey,
            StoreIds = [GatewayFixture.StoreId]
        });
}
