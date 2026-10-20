using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialDisabledTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    [Fact]
    public async Task UnconfiguredNativeGoogleIsExplicitlyUnavailableAndCannotCreateChallenges()
    {
        AuthenticateAsAnonymous();
        var bootstrap = await Client.GetFromJsonAsync<JsonElement>("/api/tenant/mobile/bootstrap?contractVersion=1");
        Assert.False(bootstrap.GetProperty("data").GetProperty("flags").GetProperty("nativeGoogleAuthV1").GetBoolean());
        var response = await Client.PostAsJsonAsync(Route + "google/challenge", new { schemaVersion = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
