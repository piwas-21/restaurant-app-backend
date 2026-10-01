using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class UberWebhookTests(GatewayFixture fixture)
{
    private const string WebhookPath = "/api/webhooks/uber-eats";

    [Fact]
    public async Task SignedNotificationIsDurableBeforeEmpty200AndSurvivesHostRestart()
    {
        var eventId = Guid.NewGuid().ToString();
        var body = Notification(eventId);
        using (var host = fixture.Host())
        using (var client = host.CreateClient())
        using (var response = await Send(client, body))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Equal(1, await fixture.Count(eventId));
        }
        using var restarted = fixture.Host();
        using var restartedClient = restarted.CreateClient();
        using var duplicate = await Send(restartedClient, body);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(1, await fixture.Count(eventId));
    }

    [Fact]
    public async Task ConcurrentRedeliveriesStoreExactlyOneReceipt()
    {
        var eventId = Guid.NewGuid().ToString();
        var body = Notification(eventId);
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Send(client, body)));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }
        Assert.Equal(1, await fixture.Count(eventId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad-signature")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task UnsignedOrForgedRequestsNeverEnterInbox(string signature)
    {
        var eventId = Guid.NewGuid().ToString();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, Notification(eventId), signature: signature);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await fixture.Count(eventId));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("")]
    public async Task ProductionOrMissingEnvironmentIsRefused(string environment)
    {
        var eventId = Guid.NewGuid().ToString();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, Notification(eventId), environment);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await fixture.Count(eventId));
    }

    [Fact]
    public async Task UnapprovedStoreIsRefusedEvenWithAValidSignature()
    {
        var eventId = Guid.NewGuid().ToString();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, Notification(eventId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await fixture.Count(eventId));
    }

    [Fact]
    public async Task SameEventIdWithADifferentBodyIsNotASuccessfulDuplicate()
    {
        var eventId = Guid.NewGuid().ToString();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var first = await Send(client, Notification(eventId));
        using var collision = await Send(client, Notification(eventId, type: "orders.cancel"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal(1, await fixture.Count(eventId));
    }

    [Fact]
    public async Task MissingConfigurationAndDatabaseFailureAreNeverAcknowledged()
    {
        using var unconfigured = fixture.Host(signingKey: string.Empty);
        using var unconfiguredClient = unconfigured.CreateClient();
        using var missing = await Send(unconfiguredClient, Notification(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);

        var unavailable = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Host = "127.0.0.1",
            Port = 1,
            Timeout = 1,
        };
        using var disconnected = fixture.Host(connectionString: unavailable.ToString());
        using var disconnectedClient = disconnected.CreateClient();
        using var failure = await Send(disconnectedClient, Notification(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
    }

    [Theory]
    [InlineData("not-json", HttpStatusCode.BadRequest)]
    [InlineData("{}", HttpStatusCode.BadRequest)]
    public async Task AuthenticatedMalformedEventsAreRefused(string body, HttpStatusCode status)
    {
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, body);
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task BodiesAboveTheLimitAreRefused()
    {
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, new string('x', 65_537));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ChunkedBodiesCannotBypassTheSizeLimit()
    {
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath);
        request.Content = new UnknownLengthContent(new byte[65_537]);
        Assert.Null(request.Content.Headers.ContentLength);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task ProvisioningEnvelopeRecordsTheStoreWithoutOrderMeta()
    {
        var eventId = Guid.NewGuid().ToString();
        var body = JsonSerializer.Serialize(new
        {
            event_type = "store.provisioned",
            store_id = GatewayFixture.StoreId,
            webhook_meta = new { client_id = GatewayFixture.ClientId, webhook_msg_uuid = eventId, webhook_msg_timestamp = 1623317195 },
        });
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await Send(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await fixture.Count(eventId));
    }

    private static string Notification(string eventId, Guid? store = null, string type = "orders.notification")
        => JsonSerializer.Serialize(new
        {
            event_id = eventId,
            event_type = type,
            event_time = 1427343990,
            meta = new { user_id = store ?? GatewayFixture.StoreId, resource_id = "153dd7f1-339d-4619-940c-418943c14636", status = "pos" },
            // A malicious href cannot make this receiver perform any network request.
            resource_href = "http://169.254.169.254/latest/meta-data/",
        });

    private static async Task<HttpResponseMessage> Send(HttpClient client, string body,
        string environment = "sandbox", string? signature = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Environment", environment);
        request.Headers.Add("X-Uber-Signature", signature ?? Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(GatewayFixture.SigningKey), Encoding.UTF8.GetBytes(body))));
        return await client.SendAsync(request);
    }
}
