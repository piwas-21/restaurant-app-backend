using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

[Collection("Database Lane 4")]
public sealed class RestaurantTipPreferencesTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Non_admin_cannot_change_presets()
    {
        var response = await Client.PutAsJsonAsync("/api/restaurant-info/tips",
            new { mode = "percentage", values = new[] { 5, 10, 15 } });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("percentage", 0, 20)]
    [InlineData("percentage", 10, 10)]
    [InlineData("percentage", 101, 20)]
    [InlineData("fixed", -100, 200)]
    public async Task Invalid_presets_are_rejected(string mode, int first, int second)
    {
        Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        var response = await Client.PutAsJsonAsync("/api/restaurant-info/tips",
            new { mode, values = new[] { first, second }, currency = "CHF" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fixed_presets_round_trip_without_changing_restaurant_identity()
    {
        await using var context = DatabaseFixture.CreateContext();
        var original = await context.RestaurantInfo.SingleAsync();
        var mode = original.TipPresetMode;
        var values = original.TipPresetValues.ToArray();
        var tipCurrency = original.TipPresetCurrency;
        try
        {
            Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
            var response = await Client.PutAsJsonAsync("/api/restaurant-info/tips",
                new { mode = "fixed", values = new[] { 100, 200, 300, 400 }, currency = original.Currency });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await response.Content.ReadFromJsonAsync<ApiResponse<TipPreferencesDto>>();
            saved!.Success.Should().BeTrue();
            saved.Data!.Values.Should().Equal(100, 200, 300, 400);
            var read = await Client.GetFromJsonAsync<ApiResponse<RestaurantInfoDto>>("/api/restaurant-info");
            read!.Data!.TipPreferences!.Mode.Should().Be("fixed");
            read.Data.TipPreferences.Currency.Should().Be(original.Currency);
            read.Data.TipPreferences.Values.Should().Equal(100, 200, 300, 400);
            read.Data.Name.Should().Be(original.Name);
            var mismatch = await Client.PutAsJsonAsync("/api/restaurant-info/tips",
                new { mode = "fixed", values = new[] { 100 }, currency = original.Currency == "CHF" ? "EUR" : "CHF" });
            mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await context.Entry(original).ReloadAsync();
            original.TipPresetMode = mode;
            original.TipPresetValues = values;
            original.TipPresetCurrency = tipCurrency;
            await context.SaveChangesAsync();
        }
    }
}
