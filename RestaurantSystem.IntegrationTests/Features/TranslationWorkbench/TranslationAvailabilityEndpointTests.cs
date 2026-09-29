using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

[Collection("Database Lane 2")]
public sealed class TranslationAvailabilityEndpointTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AvailabilityRefusesNonAdminAndReportsDisabledByDefault()
    {
        var nonAdmin = await Client.GetAsync("/api/translation-workbench/availability");
        nonAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/translation-workbench/availability");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetProperty("providerStatus")
            .GetString().Should().Be("disabled");
    }
}
