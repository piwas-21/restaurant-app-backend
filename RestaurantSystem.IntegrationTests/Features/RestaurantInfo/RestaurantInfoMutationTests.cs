using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.AddPhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdatePhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

/// <summary>
/// Verifies the mutation surface of <c>/api/restaurant-info</c>: admin can
/// edit; non-admin gets 403; phones round-trip through Add/Update/Delete;
/// malformed E.164 is rejected with 400.
/// </summary>
public class RestaurantInfoMutationTests : IntegrationTestBase
{
    public RestaurantInfoMutationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // ── Authorization ────────────────────────────────────────────────────
    // Note: TestAuthHandler always returns Success with a default Customer
    // principal (no truly-anonymous code path exists in the test infra).
    // So requests without `X-Test-Admin` are authenticated-as-Customer and
    // therefore Forbidden (403) by [RequireAdmin], not Unauthorized (401).
    // In production with JwtBearer, no token would be 401 — that path is
    // covered implicitly by the framework's default challenge.

    [Fact]
    public async Task Update_NonAdminCaller_Returns403()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/restaurant-info",
            new UpdateRestaurantInfoCommand(
                "Rumi", "Rue X 1", null, "Genève", "1202", "Switzerland",
                null, null, "test@example.com", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddPhone_NonAdminCaller_Returns403()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand(null, "+41227863333", false, 0, true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Update singleton ─────────────────────────────────────────────────

    [Fact]
    public async Task Update_AsAdmin_PersistsAndReturnsUpdated()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            "/api/restaurant-info",
            new UpdateRestaurantInfoCommand(
                Name: "Rumi Restaurant Geneva",
                AddressLine1: "Rue du Grand-Pré 99",
                AddressLine2: "2nd floor",
                City: "Genève",
                PostalCode: "1202",
                Country: "Switzerland",
                Latitude: 46.21754m,
                Longitude: 6.13905m,
                Email: "contact@rumirestaurant.ch",
                Website: "https://rumirestaurant.ch"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ReadDataAsync<RestaurantInfoDto>(response);
        dto.Name.Should().Be("Rumi Restaurant Geneva");
        dto.AddressLine1.Should().Be("Rue du Grand-Pré 99");
        dto.AddressLine2.Should().Be("2nd floor");
        dto.Latitude.Should().Be(46.21754m);
        dto.Email.Should().Be("contact@rumirestaurant.ch");
        dto.Website.Should().Be("https://rumirestaurant.ch");
    }

    // ── Phone CRUD ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddPhone_AsAdmin_AppearsInGet()
    {
        AuthenticateAsAdmin();

        var addResp = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand("Reservations", "+41227000001", true, 5, true));
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await ReadDataAsync<RestaurantPhoneNumberDto>(addResp);
        added.Number.Should().Be("+41227000001");
        added.WhatsAppEnabled.Should().BeTrue();

        // Verify GET (anon) lists it
        var getResp = await Client.GetAsync("/api/restaurant-info");
        var info = await ReadDataAsync<RestaurantInfoDto>(getResp);
        info.PhoneNumbers.Should().Contain(p => p.Id == added.Id && p.Number == "+41227000001");
    }

    [Fact]
    public async Task UpdatePhone_AsAdmin_PersistsChanges()
    {
        AuthenticateAsAdmin();

        var addResp = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand("Old label", "+41227000002", false, 0, true));
        var phone = await ReadDataAsync<RestaurantPhoneNumberDto>(addResp);

        var updateResp = await Client.PutAsJsonAsync(
            $"/api/restaurant-info/phones/{phone.Id}",
            new UpdatePhoneNumberCommand(phone.Id, "New label", "+41227000099", true, 1, false));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadDataAsync<RestaurantPhoneNumberDto>(updateResp);
        updated.Label.Should().Be("New label");
        updated.Number.Should().Be("+41227000099");
        updated.WhatsAppEnabled.Should().BeTrue();
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePhone_RouteIdOverridesBodyId()
    {
        AuthenticateAsAdmin();

        // Seed two phones; PUT to A's URL with B's id in the body — the
        // controller must apply the change to A (route id wins via
        // `command with { Id = id }`).
        var aResp = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand("A", "+41227000010", false, 0, true));
        var phoneA = await ReadDataAsync<RestaurantPhoneNumberDto>(aResp);

        var bResp = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand("B", "+41227000011", false, 0, true));
        var phoneB = await ReadDataAsync<RestaurantPhoneNumberDto>(bResp);

        var updateResp = await Client.PutAsJsonAsync(
            $"/api/restaurant-info/phones/{phoneA.Id}",
            new UpdatePhoneNumberCommand(phoneB.Id, "Updated A", "+41227000099", false, 0, true));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadDataAsync<RestaurantPhoneNumberDto>(updateResp);
        updated.Id.Should().Be(phoneA.Id);
        updated.Label.Should().Be("Updated A");

        // B must remain unchanged
        var getResp = await Client.GetAsync("/api/restaurant-info");
        var info = await ReadDataAsync<RestaurantInfoDto>(getResp);
        info.PhoneNumbers.Should().Contain(p => p.Id == phoneB.Id && p.Label == "B" && p.Number == "+41227000011");
    }

    [Fact]
    public async Task DeletePhone_AsAdmin_RemovesFromGet()
    {
        AuthenticateAsAdmin();

        var addResp = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand(null, "+41227000003", false, 0, true));
        var phone = await ReadDataAsync<RestaurantPhoneNumberDto>(addResp);

        var deleteResp = await Client.DeleteAsync($"/api/restaurant-info/phones/{phone.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await Client.GetAsync("/api/restaurant-info");
        var info = await ReadDataAsync<RestaurantInfoDto>(getResp);
        info.PhoneNumbers.Should().NotContain(p => p.Id == phone.Id);
    }

    // ── E.164 validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("0227863333")]      // missing leading +
    [InlineData("+0227863333")]     // first digit is 0
    [InlineData("+41")]             // too short
    [InlineData("+41 22 786 33 33")] // contains spaces
    [InlineData("+abc12345")]       // non-digits
    public async Task AddPhone_RejectsMalformedE164(string badNumber)
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync(
            "/api/restaurant-info/phones",
            new AddPhoneNumberCommand(null, badNumber, false, 0, true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
