using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelDecisionPrinterFeedTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    private const string PrinterEndpoint = "/api/orders/printer-feed";

    [Fact]
    public async Task DeviceFeedReceivesExplicitSourcePrintGrants_OnlyAfterVerifiedAcceptance()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsAnonymous();
        (await Client.GetAsync(PrinterEndpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AuthenticateAsDevice();
        using (var held = await ReadFeed())
        {
            held.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
                .Should().NotContain(item => item.GetProperty("id").GetGuid() == orderId);
        }
        // ApiTokenScopeFilter must see a machine token without the device key on this request.
        Client.DefaultRequestHeaders.Remove(DeviceApiKeyHeader);
        var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease))).StatusCode.Should().Be(HttpStatusCode.OK);
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsAnonymous(); AuthenticateAsDevice();
        using var accepted = await ReadFeed();
        var ticket = accepted.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == orderId);
        ticket.GetProperty("isKitchenReleased").GetBoolean().Should().BeTrue();
        var source = ticket.GetProperty("externalOrder");
        source.GetProperty("externalState").GetString().Should().Be("ACCEPTED");
        source.GetProperty("currency").GetString().Should().Be("CHF");
        source.GetProperty("merchantTotal").GetDecimal().Should().Be(5);
        source.GetProperty("reportedTax").ValueKind.Should().Be(JsonValueKind.Null);
        source.GetProperty("isSandbox").GetBoolean().Should().BeTrue();
        var actions = ticket.GetProperty("permittedActions").EnumerateArray().ToArray();
        actions.Should().HaveCount(2);
        actions.Single(action => action.GetProperty("action").GetString() == "PrintKitchen")
            .GetProperty("allowed").GetBoolean().Should().BeTrue();
        actions.Single(action => action.GetProperty("action").GetString() == "PrintReceipt")
            .GetProperty("allowed").GetBoolean().Should().BeTrue();
        ticket.GetProperty("items")[0].GetProperty("specialInstructions").GetString()
            .Should().Be("No peanuts — allergy instruction fixture.");
    }

    private async Task<JsonDocument> ReadFeed()
    {
        var response = await Client.GetAsync(PrinterEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        return document;
    }
}
