using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

[Collection("Database Lane 4")]
public sealed class AddressFieldValidationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("label", 50)]
    [InlineData("addressLine1", 200)]
    [InlineData("addressLine2", 200)]
    [InlineData("city", 100)]
    [InlineData("state", 100)]
    [InlineData("postalCode", 20)]
    [InlineData("country", 100)]
    [InlineData("phone", 20)]
    [InlineData("deliveryInstructions", 500)]
    public async Task Create_and_update_refuse_each_storage_overflow_before_touching_rows(string field, int limit)
    {
        AuthenticateAsUser();
        var id = await SeedAsync();
        var payload = Valid();
        payload[field] = new string('x', limit + 1);
        (await Client.PostAsJsonAsync("/api/Addresses", payload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        payload["id"] = id;
        (await Client.PutAsJsonAsync("/api/Addresses/" + id, payload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        var saved = await context.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == id);
        saved.Label.Should().Be("Original");
        (await context.UserAddresses.CountAsync(value => value.UserId == Guid.Parse(TestAuthHandler.UserId)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Exact_database_bounds_and_supplementary_Unicode_round_trip_through_both_http_writes()
    {
        AuthenticateAsUser();
        var label = string.Concat(Enumerable.Repeat("🍽", 50));
        var payload = Valid();
        payload["label"] = label;
        payload["addressLine1"] = new string('a', 200);
        payload["addressLine2"] = new string('b', 200);
        payload["city"] = new string('c', 100);
        payload["state"] = new string('s', 100);
        payload["postalCode"] = new string('p', 20);
        payload["country"] = new string('c', 100);
        payload["phone"] = new string('1', 20);
        payload["deliveryInstructions"] = new string('d', 500);
        using var created = await Client.PostAsJsonAsync("/api/Addresses", payload);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var id = body.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        payload["id"] = id;
        using var updated = await Client.PutAsJsonAsync("/api/Addresses/" + id, payload);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        await using var context = DatabaseFixture.CreateContext();
        var stored = await context.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == id);
        stored.Label.Should().Be(label);
        stored.AddressLine2.Should().Be(new string('b', 200));
        stored.DeliveryInstructions.Should().Be(new string('d', 500));
    }

    [Theory]
    [InlineData("label")]
    [InlineData("addressLine1")]
    [InlineData("city")]
    [InlineData("postalCode")]
    [InlineData("country")]
    public async Task Required_fields_stay_required(string field)
    {
        AuthenticateAsUser();
        var payload = Valid();
        payload[field] = " ";
        (await Client.PostAsJsonAsync("/api/Addresses", payload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Null_optional_fields_remain_supported_and_NUL_is_refused_before_postgres()
    {
        AuthenticateAsUser();
        var payload = Valid();
        payload["addressLine2"] = null;
        payload["state"] = null;
        payload["phone"] = null;
        payload["deliveryInstructions"] = null;
        (await Client.PostAsJsonAsync("/api/Addresses", payload)).StatusCode.Should().Be(HttpStatusCode.OK);
        payload["label"] = "Home\0";
        (await Client.PostAsJsonAsync("/api/Addresses", payload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("omitted", false)]
    [InlineData("null", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task Optional_default_input_resolves_to_a_non_nullable_stored_boolean(string mode, bool expected)
    {
        AuthenticateAsUser();
        var payload = Valid();
        if (mode != "omitted") payload["isDefault"] = mode == "null" ? null : expected;
        using var created = await Client.PostAsJsonAsync("/api/Addresses", payload);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = body.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        data.GetProperty("isDefault").GetBoolean().Should().Be(expected);
        payload["id"] = id;
        using var updated = await Client.PutAsJsonAsync("/api/Addresses/" + id, payload);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        using var updateBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        updateBody.RootElement.GetProperty("data").GetProperty("isDefault").GetBoolean().Should().Be(expected);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == id)).IsDefault.Should().Be(expected);
    }

    private static Dictionary<string, object?> Valid() => new()
    {
        ["label"] = "Home",
        ["addressLine1"] = "Fixture street",
        ["city"] = "Fixture city",
        ["postalCode"] = "1201",
        ["country"] = "CH"
    };
    private async Task<Guid> SeedAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var address = new UserAddress
        {
            UserId = Guid.Parse(TestAuthHandler.UserId),
            Label = "Original",
            AddressLine1 = "Fixture street",
            City = "Fixture city",
            PostalCode = "1201",
            Country = "CH",
            CreatedBy = nameof(AddressFieldValidationTests)
        };
        context.UserAddresses.Add(address);
        await context.SaveChangesAsync();
        return address.Id;
    }
}
