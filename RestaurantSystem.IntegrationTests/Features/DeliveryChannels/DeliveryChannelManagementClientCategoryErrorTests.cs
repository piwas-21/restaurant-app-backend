using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public sealed class DeliveryChannelManagementClientCategoryErrorTests
{
    [Theory]
    [InlineData("CategoryLimitExceeded")]
    [InlineData("SelectionOverrideLimitExceeded")]
    public async Task CategoryBoundsRemainActionableAcrossTheTenantGateway(string code)
    {
        using var http = new HttpClient(new ResponseHandler(code)) { BaseAddress = new Uri("https://gateway.example/") };
        var client = new DeliveryChannelManagementClient(http,
            Options.Create(new DeliveryChannelManagementSettings
            { Enabled = true, ServerCredential = new string('a', 64), TenantId = "bound-tenant" }),
            Options.Create(new DeliveryChannelSettings { Stores = [new() { StoreId = Guid.NewGuid().ToString("D") }] }));

        var error = await Assert.ThrowsAsync<BadRequestException>(() => client.Send<object>(HttpMethod.Post,
            "api/tenant-management/uber/catalogue/categories/check", Guid.NewGuid(), new { }, default));

        Assert.Equal(code, error.ErrorCode);
    }

    private sealed class ResponseHandler(string code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"{{\"errorCode\":\"{code}\",\"message\":\"Bounded selection rejected.\"}}",
                    Encoding.UTF8, "application/json")
            });
    }
}
