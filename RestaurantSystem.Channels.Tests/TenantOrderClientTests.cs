using System.Net;
using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantOrderClientTests
{
    private static readonly Guid OrderId = Guid.Parse("bb0c0edb-a53b-41dc-a666-b9ec84625193");
    private static TenantStoreBinding Store() => new() { BaseUrl = "https://tenant.example/", ApiToken = "public-fixture-only" };
    private static TenantOrderClient Client(HttpClient client) => new(new TenantChannelTransport(client,
        Microsoft.Extensions.Options.Options.Create(new TenantBridgeSettings())));
    private static TenantOrderRequest Order() => new("uber-eats", "store-fixture", "order-fixture", "ABCDE", new string('a', 64),
        "EUR", 5m, null, DateTimeOffset.Parse("2026-10-01T19:25:07+02:00"), "DELIVERY_BY_UBER", null, null,
        "Test only.", [new(Guid.NewGuid(), null, "Test meal", null, 1, 5m, 5m, "No peanuts.")]);

    [Fact]
    public async Task SendsOnlyNormalizedWireToFixedConfiguredOriginAndRequiresDurableIdentity()
    {
        var handler = new ReplyHandler(new(HttpStatusCode.Created)
        { Content = new StringContent($"{{\"orderId\":\"{OrderId}\",\"alreadyImported\":false}}") });
        using var client = new HttpClient(handler);
        var result = await Client(client).Import(Store(), Order(), default);
        Assert.Equal(OrderId, result.OrderId); Assert.False(result.AlreadyImported);
        Assert.Equal("https://tenant.example/api/delivery-channels/orders", handler.Destination);
        Assert.Equal("Bearer public-fixture-only", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("EUR", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(5m, body.RootElement.GetProperty("merchantTotal").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("reportedTax").ValueKind);
        Assert.False(body.RootElement.TryGetProperty("apiToken", out _));
        Assert.False(body.RootElement.TryGetProperty("rawPayload", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"orderId\":\"bb0c0edb-a53b-41dc-a666-b9ec84625193\"}")]
    [InlineData("{\"orderId\":\"00000000-0000-0000-0000-000000000000\",\"alreadyImported\":false}")]
    [InlineData("{\"orderId\":\"bb0c0edb-a53b-41dc-a666-b9ec84625193\",\"alreadyImported\":\"false\"}")]
    [InlineData("not-json")]
    public async Task SuccessStatusWithoutValidIdentityIsUncertain(string body)
    {
        using var client = new HttpClient(new ReplyHandler(new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => Client(client).Import(Store(), Order(), default));
        Assert.Equal(502, error.Status);
    }

    [Fact]
    public async Task RedirectAndOversizedReplyCannotConfirmOrForwardCredential()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location = new("https://foreign.example/steal");
        var handler = new ReplyHandler(response); using var client = new HttpClient(handler);
        Assert.Equal(307, (await Assert.ThrowsAsync<ChannelConsoleException>(() => Client(client).Import(Store(), Order(), default))).Status);
        Assert.Equal(1, handler.Calls);
        var big = new ReplyHandler(new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[65_537])) });
        using var bounded = new HttpClient(big);
        Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => Client(bounded).Import(Store(), Order(), default))).Status);
    }

    private sealed class ReplyHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? Destination { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Destination = request.RequestUri?.ToString(); Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
