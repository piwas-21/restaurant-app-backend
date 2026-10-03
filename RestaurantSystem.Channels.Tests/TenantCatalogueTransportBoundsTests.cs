using System.Net;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantCatalogueTransportBoundsTests
{
    private static readonly TenantStoreBinding Store = new()
    {
        BaseUrl = "https://tenant.example/",
        ApiToken = "catalogue-read-fixture"
    };

    [Theory]
    [InlineData("SelectionOverrideLimitExceeded")]
    [InlineData("CategoryLimitExceeded")]
    public async Task BoundErrorsStayActionableInsteadOfBecomingGatewayFailures(string code)
    {
        using var client = new HttpClient(new ReplyHandler(new(HttpStatusCode.BadRequest)
        { Content = new StringContent($"{{\"errorCode\":\"{code}\"}}") }));
        var transport = Create(client);

        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => transport.Post(Store,
            "/api/delivery-channels/catalogue/categories/compare-snapshot", new { }, default));

        Assert.Equal(400, error.Status);
        Assert.Equal(code, error.ErrorCode);
    }

    [Fact]
    public async Task BoundedCategoryComparisonAllowsLargeReviewedInventoryReply()
    {
        using var client = new HttpClient(new ReplyHandler(new(HttpStatusCode.OK)
        { Content = new StringContent($"{{\"inventory\":\"{new string('x', 70_000)}\"}}") }));
        var response = await Create(client).Post(Store,
            "/api/delivery-channels/catalogue/categories/compare-snapshot", new { }, default);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task CategoryComparisonStillRejectsRepliesAboveSharedTwoMiBBound()
    {
        using var client = new HttpClient(new ReplyHandler(new(HttpStatusCode.OK)
        { Content = new StringContent($"{{\"inventory\":\"{new string('x', 2_097_152)}\"}}") }));

        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => Create(client).Post(Store,
            "/api/delivery-channels/catalogue/categories/compare-snapshot", new { }, default));

        Assert.Equal(502, error.Status);
    }

    private static TenantChannelTransport Create(HttpClient client)
        => new(client, Options.Create(new TenantBridgeSettings()));

    private sealed class ReplyHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
