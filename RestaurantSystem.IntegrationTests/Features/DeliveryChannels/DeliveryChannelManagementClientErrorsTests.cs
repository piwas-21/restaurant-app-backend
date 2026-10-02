using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public sealed class DeliveryChannelManagementClientErrorsTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"untrusted proxy text\"")]
    [InlineData("not json")]
    [InlineData("{\"message\":\"invalid\\nmessage\"}")]
    public async Task UnusableGatewayErrorBodiesProduceSafeRecoveryGuidance(string body)
    {
        using var http = new HttpClient(new ResponseHandler(body)) { BaseAddress = new Uri("https://gateway.example/") };
        var client = Client(http);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => client.Send<object>(HttpMethod.Post,
            "api/tenant-management/uber/disconnect", Guid.NewGuid(), null, default));
        Assert.Equal("The delivery integration could not complete this action. Refresh its status before retrying.", error.Message);
    }

    [Fact]
    public async Task KnownActionableGatewayMessageIsPreserved()
    {
        const string Message = "Refresh the menu source before retrying.";
        using var http = new HttpClient(new ResponseHandler("{\"message\":\"" + Message + "\"}"))
        { BaseAddress = new Uri("https://gateway.example/") };
        var error = await Assert.ThrowsAsync<BadRequestException>(() => Client(http).Send<object>(HttpMethod.Post,
            "api/tenant-management/uber/disconnect", Guid.NewGuid(), null, default));
        Assert.Equal(Message, error.Message);
    }

    private static DeliveryChannelManagementClient Client(HttpClient http)
        => new(http,
            Options.Create(new DeliveryChannelManagementSettings { Enabled = true, ServerCredential = new string('a', 64), TenantId = "bound-tenant" }),
            Options.Create(new DeliveryChannelSettings { Stores = [new() { StoreId = Guid.NewGuid().ToString("D") }] }));

    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
