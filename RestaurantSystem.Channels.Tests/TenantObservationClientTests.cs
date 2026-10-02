using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantObservationClientTests
{
    [Theory]
    [InlineData("ACCEPTED", false)]
    [InlineData("CANCELED", true)]
    public async Task RequiresExactIdentityStateAndTerminalAcknowledgement(string state, bool terminal)
    {
        var id = Guid.NewGuid(); var store = new TenantStoreBinding();
        var observation = new TenantOrderObservation("uber-eats", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), state, new string('a', 64), DateTimeOffset.UtcNow);
        var transport = new Transport(ProviderJson.Encode(new { orderId = id, canonicalState = state, isTerminal = terminal }));
        var client = new TenantObservationClient(transport);
        Assert.Equal(terminal, await client.Observe(store, id, observation, default));
        Assert.Equal($"/api/delivery-channels/orders/{id:D}/observe", transport.Path); Assert.Same(observation, transport.Body);
        foreach (var body in new JsonElement?[] { null, ProviderJson.Encode(new { orderId = Guid.NewGuid(), canonicalState = state, isTerminal = terminal }),
            ProviderJson.Encode(new { orderId = id, canonicalState = "other", isTerminal = terminal }),
            ProviderJson.Encode(new { orderId = id, canonicalState = state, isTerminal = !terminal }),
            ProviderJson.Encode(new { orderId = 7, canonicalState = state, isTerminal = terminal }), ProviderJson.Encode(new[] { 1 }) })
        {
            transport.Reply = body;
            Assert.Equal(502, (await Assert.ThrowsAsync<ChannelConsoleException>(() => client.Observe(store, id, observation, default))).Status);
        }
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
