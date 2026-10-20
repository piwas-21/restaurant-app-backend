using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class BillDiscountConfigurationCompatibilityTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var info = await context.RestaurantInfo.FirstAsync();
        info.Currency = "EUR";
        info.BillChargeRulesJson = JsonSerializer.Serialize(new[] { Rule("Reduce", "Percentage") }, BillChargeRules.JsonOptions);
        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Reduce", "Fixed")]
    [InlineData("Increase", "Percentage")]
    [InlineData("Reduce", "Percentage")]
    public async Task Old_admin_payload_cannot_silently_reset_advanced_modes(string effect, string calculation)
    {
        AuthenticateAsAdmin();
        await Save(Rule(effect, calculation));
        var before = await StoredJson();
        // Match an old schema that strips unknown fields rather than sending nulls.
        var response = await Client.PutAsJsonAsync("/api/bill-charges", new
        {
            currency = "EUR",
            rules = new[] { new { key = "discount", label = "Edited", enabled = true,
                defaultAmount = 10m, allowCustomAmount = true, orderTypes = new[] { "Takeaway" }, roles = new[] { "Cashier" } } }
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StoredJson()).Should().Be(before);
    }

    [Fact]
    public async Task Legacy_fixed_increase_payload_saves_and_explicit_downgrade_is_allowed()
    {
        AuthenticateAsAdmin();
        await Save(Rule("Increase", "Fixed"));
        await Save(Rule(null, null));
        var stored = BillChargeRules.Read(await StoredJson()).Single();
        stored.Effect.Should().Be("Increase");
        stored.Calculation.Should().Be("Fixed");
        using var json = JsonDocument.Parse(await StoredJson());
        json.RootElement[0].GetProperty("effect").GetString().Should().Be("Increase");
        json.RootElement[0].GetProperty("calculation").GetString().Should().Be("Fixed");
    }

    private async Task Save(BillChargeRuleDto rule)
    {
        var response = await Client.PutAsJsonAsync("/api/bill-charges", new BillChargeConfigurationDto("EUR", [rule]), JsonOptions);
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<ApiResponse<BillChargeConfigurationDto>>(JsonOptions))!.Data!;
        data.Rules.Single().Effect.Should().Be(rule.Effect ?? "Increase");
        data.Rules.Single().Calculation.Should().Be(rule.Calculation ?? "Fixed");
    }

    private async Task<string> StoredJson()
    {
        await using var context = DatabaseFixture.CreateContext();
        return (await context.RestaurantInfo.FirstAsync()).BillChargeRulesJson;
    }

    private static BillChargeRuleDto Rule(string? effect, string? calculation) =>
        new("discount", "Discount", true, 10m, true, ["Takeaway"], ["Cashier"], effect, calculation);
}
