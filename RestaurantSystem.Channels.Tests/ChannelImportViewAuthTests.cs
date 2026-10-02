using System.Net;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelImportViewAuthTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task AnonymousCannotReadJobsAndConsoleSessionCanReadDisabledSummary()
    {
        using var anonymous = await Client.GetAsync("/api/sandbox/uber/imports"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await Login(); using var authenticated = await Client.GetAsync("/api/sandbox/uber/imports");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var body = await Json(authenticated); Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength()); Assert.Empty(Provider.Calls);
    }
}
