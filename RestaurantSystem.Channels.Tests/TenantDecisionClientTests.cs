using System.Text.Json;
using System.Text.Json.Nodes;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantDecisionClientTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Guid StoreId = Guid.Parse("ee00b299-aab7-4963-9cb8-a96c0a59e1fe");
    private static TenantStoreBinding Store() => new() { StoreId = StoreId };
    private static TenantDecisionLease Lease() => new()
    {
        DecisionId = Guid.NewGuid(),
        LeaseId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2),
        Provider = "uber-eats",
        StoreId = StoreId.ToString(),
        ExternalOrderId = Guid.NewGuid().ToString(),
        Action = "accept",
        Reason = "Public test reason.",
        Attempt = 1,
    };

    [Fact]
    public async Task ValidLeaseAndReportUseFixedRoutesAndRequireExpectedOrderActionState()
    {
        var lease = Lease(); var transport = new Transport(JsonSerializer.SerializeToElement(lease, Wire));
        var client = new TenantDecisionClient(transport, TimeProvider.System);
        Assert.Equal(lease, await client.Claim(Store(), default)); Assert.Equal("/api/delivery-channels/decisions/claim", transport.Path);
        var report = new TenantDecisionReport(lease.LeaseId, "Succeeded", "ACCEPTED", new string('a', 64), DateTimeOffset.UtcNow);
        transport.Reply = ProviderJson.Encode(new { orderId = lease.OrderId, operationId = Guid.NewGuid(), action = lease.Action, state = "Succeeded" });
        await client.Report(Store(), lease, report, default);
        Assert.Equal($"/api/delivery-channels/decisions/{lease.DecisionId:D}/report", transport.Path); Assert.Same(report, transport.Body);
        transport.Reply = ProviderJson.Encode(new { orderId = Guid.NewGuid(), operationId = Guid.NewGuid(), action = "accept", state = "Succeeded" });
        Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => client.Report(Store(), lease, report, default))).Status);
    }

    [Fact]
    public async Task NullOrNoContentMeansNoLeaseButMalformedBodiesNeverDo()
    {
        var transport = new Transport(null); var client = new TenantDecisionClient(transport, TimeProvider.System);
        Assert.Null(await client.Claim(Store(), default)); transport.Reply = JsonSerializer.SerializeToElement<object?>(null);
        Assert.Null(await client.Claim(Store(), default));
        foreach (var body in new[] { "{}", "[]", "{\"leaseId\":123}", "\"text\"" })
        {
            transport.Reply = JsonDocument.Parse(body).RootElement.Clone();
            Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => client.Claim(Store(), default))).Status);
        }
    }

    [Theory]
    [InlineData("provider", "another-provider")]
    [InlineData("storeId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("externalOrderId", "../foreign-store")]
    [InlineData("action", "refund")]
    [InlineData("reason", "")]
    [InlineData("reason", "line\nbreak")]
    [InlineData("leaseUntil", "2020-01-01T00:00:00Z")]
    [InlineData("orderId", "00000000-0000-0000-0000-000000000000")]
    public async Task WrongBindingExpiredOrUnsafeLeaseIsRefusedBeforeProviderCall(string field, string value)
    {
        var body = JsonSerializer.SerializeToNode(Lease(), Wire)!; body[field] = value;
        var client = new TenantDecisionClient(new Transport(JsonSerializer.SerializeToElement(body)), TimeProvider.System);
        Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => client.Claim(Store(), default))).Status);
    }

    [Fact]
    public async Task MalformedScalarReportIdentitiesAreRefusedWithoutRuntimeTypeErrors()
    {
        var lease = Lease(); var report = new TenantDecisionReport(lease.LeaseId, "Unknown", "UNKNOWN", "", DateTimeOffset.UtcNow);
        var client = new TenantDecisionClient(new Transport(ProviderJson.Encode(new { orderId = 7, operationId = 9 })), TimeProvider.System);
        Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => client.Report(Store(), lease, report, default))).Status);
    }

    private sealed class Transport(JsonElement? reply) : ITenantChannelTransport
    {
        public JsonElement? Reply { get; set; } = reply;
        public string? Path { get; private set; }
        public object? Body { get; private set; }
        public Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken)
        { Path = path; Body = body; return Task.FromResult(Reply); }
    }
}
