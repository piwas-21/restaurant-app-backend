using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

/// <summary>
/// Verifies <c>GET /api/restaurant-info</c> returns the singleton row seeded
/// by the AddRestaurantInfo migration. Anonymous endpoint — no auth required.
/// </summary>
public class GetRestaurantInfoTests : IntegrationTestBase
{
    public GetRestaurantInfoTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Get_ReturnsSeededSingleton_Anonymously()
    {
        var response = await Client.GetAsync("/api/restaurant-info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<RestaurantInfoDto>>(json, JsonOptions);

        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();

        var info = envelope.Data!;
        // Specific assertions against the migration seed values — these
        // act as a regression guard if the seed is altered in another
        // migration without intent.
        info.Name.Should().Be("Rumi Restaurant");
        info.AddressLine1.Should().Be("Rue du Grand-Pré 45");
        info.City.Should().Be("Genève");
        info.PostalCode.Should().Be("1202");
        info.Country.Should().Be("Switzerland");
        info.Email.Should().Be("rumigeneve@gmail.com");

        info.PhoneNumbers.Should().ContainSingle();
        var seededPhone = info.PhoneNumbers[0];
        seededPhone.Number.Should().Be("+41227863333");
        seededPhone.Label.Should().Be("Reception");
        seededPhone.IsActive.Should().BeTrue();
    }
}
